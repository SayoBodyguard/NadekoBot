﻿using Discord.Commands;
using NadekoBot.Modules.Music.Classes;
using System.Collections.Concurrent;
using Discord.WebSocket;
using NadekoBot.Services;
using System.IO;
using Discord;
using System.Threading.Tasks;
using NadekoBot.Attributes;
using System;
using System.Linq;
using NadekoBot.Extensions;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using NadekoBot.Services.Database;

namespace NadekoBot.Modules.Music
{
    [Module("!!", AppendSpace = false)]
    public partial class Music : DiscordModule
    {
        public static ConcurrentDictionary<ulong, MusicPlayer> MusicPlayers = new ConcurrentDictionary<ulong, MusicPlayer>();

        public const string MusicDataPath = "data/musicdata";
        private IGoogleApiService _google;

        public Music(ILocalization loc, CommandService cmds, DiscordSocketClient client, IGoogleApiService google) : base(loc, cmds, client)
        {
            //it can fail if its currenctly opened or doesn't exist. Either way i don't care
            try { Directory.Delete(MusicDataPath, true); } catch { }

            Directory.CreateDirectory(MusicDataPath);

            _google = google;
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Next(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer)) return;
            if (musicPlayer.PlaybackVoiceChannel == ((IGuildUser)umsg.Author).VoiceChannel)
                musicPlayer.Next();
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Stop(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer)) return;
            if (((IGuildUser)umsg.Author).VoiceChannel == musicPlayer.PlaybackVoiceChannel)
            {
                musicPlayer.Autoplay = false;
                musicPlayer.Stop();
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Destroy(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryRemove(channel.Guild.Id, out musicPlayer)) return;
            if (((IGuildUser)umsg.Author).VoiceChannel == musicPlayer.PlaybackVoiceChannel)
                musicPlayer.Destroy();
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Pause(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer)) return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            musicPlayer.TogglePause();
            if (musicPlayer.Paused)
                await channel.SendMessageAsync("🎵`Music Player paused.`").ConfigureAwait(false);
            else
                await channel.SendMessageAsync("🎵`Music Player unpaused.`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Queue(IUserMessage umsg, [Remainder] string query)
        {
            var channel = (ITextChannel)umsg.Channel;

            await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, query).ConfigureAwait(false);
            if (channel.Guild.GetCurrentUser().GetPermissions(channel).ManageMessages)
            {
                await Task.Delay(10000).ConfigureAwait(false);
                await ((IUserMessage)umsg).DeleteAsync().ConfigureAwait(false);
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task SoundCloudQueue(IUserMessage umsg, [Remainder] string query)
        {
            var channel = (ITextChannel)umsg.Channel;

            await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, query, musicType: MusicType.Soundcloud).ConfigureAwait(false);
            if (channel.Guild.GetCurrentUser().GetPermissions(channel).ManageMessages)
            {
                await Task.Delay(10000).ConfigureAwait(false);
                await ((IUserMessage)umsg).DeleteAsync().ConfigureAwait(false);
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task ListQueue(IUserMessage umsg, int page = 1)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
            {
                await channel.SendMessageAsync("🎵 No active music player.").ConfigureAwait(false);
                return;
            }
            if (page <= 0)
                return;

            var currentSong = musicPlayer.CurrentSong;
            if (currentSong == null)
                return;
            var toSend = $"🎵`Now Playing` {currentSong.PrettyName} " + $"{currentSong.PrettyCurrentTime()}\n";
            if (musicPlayer.RepeatSong)
                toSend += "🔂";
            else if (musicPlayer.RepeatPlaylist)
                toSend += "🔁";
            toSend += $" **{musicPlayer.Playlist.Count}** `tracks currently queued. Showing page {page}` ";
            if (musicPlayer.MaxQueueSize != 0 && musicPlayer.Playlist.Count >= musicPlayer.MaxQueueSize)
                toSend += "**Song queue is full!**\n";
            else
                toSend += "\n";
            const int itemsPerPage = 15;
            int startAt = itemsPerPage * (page - 1);
            var number = 1 + startAt;
            await channel.SendMessageAsync(toSend + string.Join("\n", musicPlayer.Playlist.Skip(startAt).Take(15).Select(v => $"`{number++}.` {v.PrettyName}"))).ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task NowPlaying(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            var currentSong = musicPlayer.CurrentSong;
            if (currentSong == null)
                return;
            await channel.SendMessageAsync($"🎵`Now Playing` {currentSong.PrettyName} " +
                                        $"{currentSong.PrettyCurrentTime()}").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Volume(IUserMessage umsg, int val)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            if (val < 0)
                return;
            var volume = musicPlayer.SetVolume(val);
            await channel.SendMessageAsync($"🎵 `Volume set to {volume}%`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Defvol(IUserMessage umsg, [Remainder] int val)
        {
            var channel = (ITextChannel)umsg.Channel;

            if (val < 0 || val > 100)
            {
                await channel.SendMessageAsync("Volume number invalid. Must be between 0 and 100").ConfigureAwait(false);
                return;
            }
            using (var uow = DbHandler.UnitOfWork())
            {
                uow.GuildConfigs.For(channel.Guild.Id).DefaultMusicVolume = val / 100.0f;
                uow.Complete();
            }
            await channel.SendMessageAsync($"🎵 `Default volume set to {val}%`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Mute(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            musicPlayer.SetVolume(0);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Max(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            musicPlayer.SetVolume(100);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Shuffle(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            if (musicPlayer.Playlist.Count < 2)
            {
                await channel.SendMessageAsync("💢 Not enough songs in order to perform the shuffle.").ConfigureAwait(false);
                return;
            }

            musicPlayer.Shuffle();
            await channel.SendMessageAsync("🎵 `Songs shuffled.`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Playlist(IUserMessage umsg, [Remainder] string playlist)
        {
            var channel = (ITextChannel)umsg.Channel;
            var arg = playlist;
            if (string.IsNullOrWhiteSpace(arg))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel?.Guild != channel.Guild)
            {
                await channel.SendMessageAsync("💢 You need to be in a voice channel on this server.\n If you are already in a voice channel, try rejoining it.").ConfigureAwait(false);
                return;
            }
            var plId = (await _google.GetPlaylistIdsByKeywordsAsync(arg).ConfigureAwait(false)).FirstOrDefault();
            if (plId == null)
            {
                await channel.SendMessageAsync("No search results for that query.");
                return;
            }
            var ids = await _google.GetPlaylistTracksAsync(plId, 500).ConfigureAwait(false);
            if (!ids.Any())
            {
                await channel.SendMessageAsync($"🎵 `Failed to find any songs.`").ConfigureAwait(false);
                return;
            }
            var idArray = ids as string[] ?? ids.ToArray();
            var count = idArray.Length;
            var msg =
                await channel.SendMessageAsync($"🎵 `Attempting to queue {count} songs".SnPl(count) + "...`").ConfigureAwait(false);
            foreach (var id in idArray)
            {
                try
                {
                    await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, id, true).ConfigureAwait(false);
                }
                catch (PlaylistFullException)
                { break; }
                catch { }
            }
            await msg.ModifyAsync(m => m.Content = "🎵 `Playlist queue complete.`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task SoundCloudPl(IUserMessage umsg, [Remainder] string pl)
        {
            var channel = (ITextChannel)umsg.Channel;
            pl = pl?.Trim();

            if (string.IsNullOrWhiteSpace(pl))
                return;

            using (var http = new HttpClient())
            {
                var scvids = JObject.Parse(await http.GetStringAsync($"http://api.soundcloud.com/resolve?url={pl}&client_id={NadekoBot.Credentials.SoundCloudClientId}").ConfigureAwait(false))["tracks"].ToObject<SoundCloudVideo[]>();
                await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, scvids[0].TrackLink).ConfigureAwait(false);

                MusicPlayer mp;
                if (!MusicPlayers.TryGetValue(channel.Guild.Id, out mp))
                    return;

                foreach (var svideo in scvids.Skip(1))
                {
                    try
                    {
                        mp.AddSong(new Song(new Classes.SongInfo
                        {
                            Title = svideo.FullName,
                            Provider = "SoundCloud",
                            Uri = svideo.StreamLink,
                            ProviderType = MusicType.Normal,
                            Query = svideo.TrackLink,
                        }), ((IGuildUser)umsg.Author).Username);
                    }
                    catch (PlaylistFullException) { break; }
                }
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task LocalPl(IUserMessage umsg, [Remainder] string directory)
        {
            var channel = (ITextChannel)umsg.Channel;
            var arg = directory;
            if (string.IsNullOrWhiteSpace(arg))
                return;
            try
            {
                var fileEnum = new DirectoryInfo(arg).GetFiles()
                                    .Where(x => !x.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System));
                foreach (var file in fileEnum)
                {
                    try
                    {
                        await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, file.FullName, true, MusicType.Local).ConfigureAwait(false);
                    }
                    catch (PlaylistFullException)
                    {
                        break;
                    }
                    catch { }
                }
                await channel.SendMessageAsync("🎵 `Directory queue complete.`").ConfigureAwait(false);
            }
            catch { }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Radio(IUserMessage umsg, string radio_link)
        {
            var channel = (ITextChannel)umsg.Channel;
            if (((IGuildUser)umsg.Author).VoiceChannel?.Guild != channel.Guild)
            {
                await channel.SendMessageAsync("💢 You need to be in a voice channel on this server.\n If you are already in a voice channel, try rejoining it.").ConfigureAwait(false);
                return;
            }
            await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, radio_link, musicType: MusicType.Radio).ConfigureAwait(false);
            if (channel.Guild.GetCurrentUser().GetPermissions(channel).ManageMessages)
            {
                await Task.Delay(10000).ConfigureAwait(false);
                await ((IUserMessage)umsg).DeleteAsync().ConfigureAwait(false);
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Local(IUserMessage umsg, [Remainder] string path)
        {
            var channel = (ITextChannel)umsg.Channel;
            var arg = path;
            if (string.IsNullOrWhiteSpace(arg))
                return;
            await QueueSong(((IGuildUser)umsg.Author), channel, ((IGuildUser)umsg.Author).VoiceChannel, path, musicType: MusicType.Local).ConfigureAwait(false);

        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Move(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            var voiceChannel = ((IGuildUser)umsg.Author).VoiceChannel;
            if (voiceChannel == null || voiceChannel.Guild != channel.Guild || !MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            musicPlayer.MoveToVoiceChannel(voiceChannel);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Remove(IUserMessage umsg, int num)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
            {
                return;
            }
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;
            if (num <= 0 || num > musicPlayer.Playlist.Count)
                return;
            var song = (musicPlayer.Playlist as List<Song>)?[num - 1];
            musicPlayer.RemoveSongAt(num - 1);
            await channel.SendMessageAsync($"🎵**Track {song.PrettyName} at position `#{num}` has been removed.**").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Remove(IUserMessage umsg, string all)
        {
            var channel = (ITextChannel)umsg.Channel;

            if (all.Trim().ToUpperInvariant() != "ALL")
                return;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer)) return;
            musicPlayer.ClearQueue();
            await channel.SendMessageAsync($"🎵`Queue cleared!`").ConfigureAwait(false);
            return;
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task MoveSong(IUserMessage umsg, [Remainder] string fromto)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
            {
                return;
            }
            fromto = fromto?.Trim();
            var fromtoArr = fromto.Split('>');

            int n1;
            int n2;

            var playlist = musicPlayer.Playlist as List<Song> ?? musicPlayer.Playlist.ToList();

            if (fromtoArr.Length != 2 || !int.TryParse(fromtoArr[0], out n1) ||
                !int.TryParse(fromtoArr[1], out n2) || n1 < 1 || n2 < 1 || n1 == n2 ||
                n1 > playlist.Count || n2 > playlist.Count)
            {
                await channel.SendMessageAsync("`Invalid input.`").ConfigureAwait(false);
                return;
            }

            var s = playlist[n1 - 1];
            playlist.Insert(n2 - 1, s);
            var nn1 = n2 < n1 ? n1 : n1 - 1;
            playlist.RemoveAt(nn1);

            await channel.SendMessageAsync($"🎵`Moved` {s.PrettyName} `from #{n1} to #{n2}`").ConfigureAwait(false);


        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task SetMaxQueue(IUserMessage umsg, uint size)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
            {
                return;
            }
            musicPlayer.MaxQueueSize = size;
            await channel.SendMessageAsync($"🎵 `Max queue set to {(size == 0 ? ("unlimited") : size + " tracks")}`");
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task ReptCurSong(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            var currentSong = musicPlayer.CurrentSong;
            if (currentSong == null)
                return;
            var currentValue = musicPlayer.ToggleRepeatSong();
            await channel.SendMessageAsync(currentValue ?
                                        $"🎵🔂`Repeating track:`{currentSong.PrettyName}" :
                                        $"🎵🔂`Current track repeat stopped.`")
                                            .ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task RepeatPl(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            var currentValue = musicPlayer.ToggleRepeatPlaylist();
            await channel.SendMessageAsync($"🎵🔁`Repeat playlist {(currentValue ? "enabled" : "disabled")}`").ConfigureAwait(false);
        }

        ///
        //[LocalizedCommand, LocalizedDescription, LocalizedSummary]
        //[RequireContext(ContextType.Guild)]
        //public async Task Save(IUserMessage umsg, [Remainder] string name)
        //{
        //    var channel = (ITextChannel)umsg.Channel;

        //}

        //[LocalizedCommand, LocalizedDescription, LocalizedSummary]
        //[RequireContext(ContextType.Guild)]
        //public async Task Load(IUserMessage umsg, [Remainder] string name)
        //{
        //    var channel = (ITextChannel)umsg.Channel;

        //}

        //[LocalizedCommand, LocalizedDescription, LocalizedSummary]
        //[RequireContext(ContextType.Guild)]
        //public async Task Playlists(IUserMessage umsg, [Remainder] string num)
        //{
        //    var channel = (ITextChannel)umsg.Channel;

        //}

        //[LocalizedCommand, LocalizedDescription, LocalizedSummary]
        //[RequireContext(ContextType.Guild)]
        //public async Task DeletePlaylist(IUserMessage umsg, [Remainder] string pl)
        //{
        //    var channel = (ITextChannel)umsg.Channel;

        //}

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Goto(IUserMessage umsg, int time)
        {
            var channel = (ITextChannel)umsg.Channel;

            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;
            if (((IGuildUser)umsg.Author).VoiceChannel != musicPlayer.PlaybackVoiceChannel)
                return;

            if (time < 0)
                return;

            var currentSong = musicPlayer.CurrentSong;

            if (currentSong == null)
                return;

            //currentSong.PrintStatusMessage = false;
            var gotoSong = currentSong.Clone();
            gotoSong.SkipTo = time;
            musicPlayer.AddSong(gotoSong, 0);
            musicPlayer.Next();

            var minutes = (time / 60).ToString();
            var seconds = (time % 60).ToString();

            if (minutes.Length == 1)
                minutes = "0" + minutes;
            if (seconds.Length == 1)
                seconds = "0" + seconds;

            await channel.SendMessageAsync($"`Skipped to {minutes}:{seconds}`").ConfigureAwait(false);
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task GetLink(IUserMessage umsg, int index = 0)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;

            if (index < 0)
                return;

            if (index > 0)
            {

                var selSong = musicPlayer.Playlist.DefaultIfEmpty(null).ElementAtOrDefault(index - 1);
                if (selSong == null)
                {
                    await channel.SendMessageAsync("Could not select song, likely wrong index");

                }
                else
                {
                    await channel.SendMessageAsync($"🎶`Selected song {selSong.SongInfo.Title}:` <{selSong.SongInfo.Query}>").ConfigureAwait(false);
                }
            }
            else
            {
                var curSong = musicPlayer.CurrentSong;
                if (curSong == null)
                    return;
                await channel.SendMessageAsync($"🎶`Current song:` <{curSong.SongInfo.Query}>").ConfigureAwait(false);
            }
        }

        [LocalizedCommand, LocalizedDescription, LocalizedSummary]
        [RequireContext(ContextType.Guild)]
        public async Task Autoplay(IUserMessage umsg)
        {
            var channel = (ITextChannel)umsg.Channel;
            MusicPlayer musicPlayer;
            if (!MusicPlayers.TryGetValue(channel.Guild.Id, out musicPlayer))
                return;

            if (!musicPlayer.ToggleAutoplay())
                await channel.SendMessageAsync("🎶`Autoplay disabled.`").ConfigureAwait(false);
            else
                await channel.SendMessageAsync("🎶`Autoplay enabled.`").ConfigureAwait(false);
        }

        public static async Task QueueSong(IGuildUser queuer, ITextChannel textCh, IVoiceChannel voiceCh, string query, bool silent = false, MusicType musicType = MusicType.Normal)
        {
            if (voiceCh == null || voiceCh.Guild != textCh.Guild)
            {
                if (!silent)
                    await textCh.SendMessageAsync("💢 You need to be in a voice channel on this server.\n If you are already in a voice channel, try rejoining.").ConfigureAwait(false);
                throw new ArgumentNullException(nameof(voiceCh));
            }
            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
                throw new ArgumentException("💢 Invalid query for queue song.", nameof(query));

            var musicPlayer = MusicPlayers.GetOrAdd(textCh.Guild.Id, server =>
            {
                float vol = 1;// SpecificConfigurations.Default.Of(server.Id).DefaultMusicVolume;
                using (var uow = DbHandler.UnitOfWork())
                {
                    vol = uow.GuildConfigs.For(textCh.Guild.Id).DefaultMusicVolume;
                }
                var mp = new MusicPlayer(voiceCh, vol);


                IUserMessage playingMessage = null;
                IUserMessage lastFinishedMessage = null;
                mp.OnCompleted += async (s, song) =>
                {
                    if (song.PrintStatusMessage)
                    {
                        try
                        {
                            if (lastFinishedMessage != null)
                                await lastFinishedMessage.DeleteAsync().ConfigureAwait(false);
                            if (playingMessage != null)
                                await playingMessage.DeleteAsync().ConfigureAwait(false);
                            lastFinishedMessage = await textCh.SendMessageAsync($"🎵`Finished`{song.PrettyName}").ConfigureAwait(false);
                            if (mp.Autoplay && mp.Playlist.Count == 0 && song.SongInfo.Provider == "YouTube")
                            {
                                await QueueSong(queuer.Guild.GetCurrentUser(), textCh, voiceCh, (await NadekoBot.Google.GetRelatedVideosAsync(song.SongInfo.Query, 4)).ToList().Shuffle().FirstOrDefault(), silent, musicType).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                };
                mp.OnStarted += async (s, song) =>
                {
                    if (song.PrintStatusMessage)
                    {
                        var sender = s as MusicPlayer;
                        if (sender == null)
                            return;

                        try
                        {

                            var msgTxt = $"🎵`Playing`{song.PrettyName} `Vol: {(int)(sender.Volume * 100)}%`";
                            playingMessage = await textCh.SendMessageAsync(msgTxt).ConfigureAwait(false);
                        }
                        catch { }
                    }
                };
                return mp;
            });
            Song resolvedSong;
            try
            {
                musicPlayer.ThrowIfQueueFull();
                resolvedSong = await Song.ResolveSong(query, musicType).ConfigureAwait(false);

                musicPlayer.AddSong(resolvedSong, queuer.Username);
            }
            catch (PlaylistFullException)
            {
                await textCh.SendMessageAsync($"🎵 `Queue is full at {musicPlayer.MaxQueueSize}/{musicPlayer.MaxQueueSize}.` ");
                throw;
            }
            if (!silent)
            {
                var queuedMessage = await textCh.SendMessageAsync($"🎵`Queued`{resolvedSong.PrettyName} **at** `#{musicPlayer.Playlist.Count + 1}`").ConfigureAwait(false);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(async () =>
                {
                    await Task.Delay(10000).ConfigureAwait(false);
                    try
                    {
                        await queuedMessage.DeleteAsync().ConfigureAwait(false);
                    }
                    catch { }
                }).ConfigureAwait(false);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
        }
    }
}