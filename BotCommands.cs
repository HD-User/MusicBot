using System.Web;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Lavalink;

namespace MusicBot;

public class BotCommands : BaseCommandModule
{
    public BotCommands()
    {
        musicQueue = new Dictionary<ulong, Queue<LavalinkTrack>>();
        OnPlaybackFinishHandlers = new Dictionary<ulong, Thread>();
        ReconnectionInProgress = new List<ulong>();
        Repeat = new Dictionary<ulong, LavalinkTrack>();

#if DEBUG
        new Thread(() =>
        {
            while (true)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("MusicQueue: {0}", musicQueue.Count);
                Console.WriteLine("OPFHandlers: {0}", OnPlaybackFinishHandlers.Count);
                Console.WriteLine("ReconnectionInProgress: {0}", ReconnectionInProgress.Count);

                Thread.Sleep(3000);
            }
        }).Start();
#endif
    }

    public static Dictionary<ulong, Queue<LavalinkTrack>> musicQueue { get; set; }
    public static Dictionary<ulong, Thread> OnPlaybackFinishHandlers { get; set; }
    public static List<ulong> ReconnectionInProgress { get; set; }
    public static Dictionary<ulong, LavalinkTrack> Repeat { get; set; }

    [Command("play")]
    [Aliases("p")]
    public async Task PlayMusic(CommandContext ctx, [RemainingText] string query)
    {
        DiscordChannel userVc;
        if (ctx.Member?.VoiceState != null)
        {
            userVc = ctx.Member.VoiceState.Channel;
        }
        else
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (userVc == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVc.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        //Connecting to the VC and playing music
        var node = lavalinkInstance.ConnectedNodes.Values.First();
        await node.ConnectAsync(userVc);

        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);
        if (conn == null)
        {
            await ctx.RespondAsync("Failed to connect to the VC.");
            return;
        }

        //Validating if the query is a valid link or a search query
        if (Uri.TryCreate(query, UriKind.Absolute, out var url))
        {
            string urlText = url.AbsoluteUri;

            try
            {
                var urlParameters = HttpUtility.ParseQueryString(query);
                urlParameters.Remove("list");
                if (!Uri.TryCreate(urlParameters.ToString(), UriKind.Absolute, out url))
                {
                    throw new UriFormatException();
                }
                urlText = url.AbsoluteUri;
            }
            catch (UriFormatException) { }

            var urlResult = await node.Rest.GetTracksAsync(url);

            if (urlResult.LoadResultType is LavalinkLoadResultType.LoadFailed or LavalinkLoadResultType.NoMatches)
            {
                await ctx.RespondAsync($"Failed to find music with URL: {urlText}");
            }

            else if (urlResult.LoadResultType == LavalinkLoadResultType.PlaylistLoaded && query.Contains("playlist"))
            {
                if (!musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
                    musicQueue.Add(ctx.Member.VoiceState.Guild.Id, new Queue<LavalinkTrack>());

                foreach (var track in urlResult.Tracks)
                {
                    musicQueue[ctx.Member.VoiceState.Guild.Id].Enqueue(track);
                }

                if (conn.CurrentState.CurrentTrack == null)
                {
                    var track = musicQueue[ctx.Member.VoiceState.Guild.Id].Dequeue();
                    if (musicQueue[ctx.Member.VoiceState.Guild.Id].Count == 0) musicQueue.Remove(ctx.Member.VoiceState.Guild.Id);
                    await conn.PlayAsync(track);

                    var musicDescription = $"Now Playing: {track.Title} \n" +
                                           $"Author: {track.Author} \n" +
                                           $"URL: {track.Uri}";

                    await ctx.Channel.SendMessageAsync(new DiscordEmbedBuilder
                    {
                        Color = DiscordColor.Green,
                        Title = $"Successfully joined channel {userVc.Name} and added playlist {urlResult.PlaylistInfo.Name} to the queue.",
                        Description = musicDescription
                    });
                }
                else
                {
                    await ctx.Channel.SendMessageAsync(new DiscordEmbedBuilder
                    {
                        Title = $"Playlist {urlResult.PlaylistInfo.Name} has been added to the queue.",
                        Color = DiscordColor.Green
                    });
                }
            }
            else
            {
                var track = urlResult.Tracks.First();
                if (conn.CurrentState.CurrentTrack == null)
                {
                    await conn.PlayAsync(track);

                    var musicDescription = $"Now Playing: {track.Title} \n" +
                                           $"Author: {track.Author} \n" +
                                           $"URL: {track.Uri}";
                    var nowPlayingEmbed = new DiscordEmbedBuilder
                    {
                        Color = DiscordColor.Green,
                        Title = $"Successfully joined channel {userVc.Name} and playing music",
                        Description = musicDescription
                    };

                    await ctx.Channel.SendMessageAsync(nowPlayingEmbed);
                }
                else
                {
                    if (!musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
                        musicQueue.Add(ctx.Member.VoiceState.Guild.Id, new Queue<LavalinkTrack>());
                    musicQueue[ctx.Member.VoiceState.Guild.Id].Enqueue(track);
                    await ctx.RespondAsync($"Added to queue: {track.Title}");
                }
            }
        }

        else
        {
            var searchQuery = await node.Rest.GetTracksAsync(query);
            if (searchQuery.LoadResultType is LavalinkLoadResultType.NoMatches or LavalinkLoadResultType.LoadFailed)
            {
                await ctx.RespondAsync($"Failed to find music with query: {query}");
                return;
            }

            var musicTrack = searchQuery.Tracks.First();

            if (conn.CurrentState.CurrentTrack == null)
            {
                await conn.PlayAsync(musicTrack);

                var musicDescription = $"Now Playing: {musicTrack.Title} \n" +
                                       $"Author: {musicTrack.Author} \n" +
                                       $"URL: {musicTrack.Uri}";
                var nowPlayingEmbed = new DiscordEmbedBuilder
                {
                    Color = DiscordColor.Green,
                    Title = $"Successfully joined channel {userVc.Name} and playing music",
                    Description = musicDescription
                };

                await ctx.Channel.SendMessageAsync(nowPlayingEmbed);
            }
            else
            {
                if (!musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
                    musicQueue.Add(ctx.Member.VoiceState.Guild.Id, new Queue<LavalinkTrack>());
                musicQueue[ctx.Member.VoiceState.Guild.Id].Enqueue(musicTrack);
                await ctx.RespondAsync($"Added to queue: {musicTrack.Title}");
            }
        }

        foreach (var t in OnPlaybackFinishHandlers.Where(t => !t.Value.IsAlive)) OnPlaybackFinishHandlers.Remove(t.Key);

        if (!OnPlaybackFinishHandlers.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var thread))
            PlaybackFinishedManager(conn);
    }

    [Command("search")]
    public async Task SearchMusic(CommandContext ctx, [RemainingText] string query)
    {
        DiscordChannel? userVc;
        var interactivity = Program.Client.GetInteractivity();
        if (ctx.Member?.VoiceState != null)
        {
            userVc = ctx.Member.VoiceState.Channel;
        }
        else
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (userVc == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVc.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        //Connecting to the VC and playing music
        var node = lavalinkInstance.ConnectedNodes.Values.First();
        await node.ConnectAsync(userVc);
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);
        if (conn == null)
        {
            await ctx.RespondAsync("Failed to connect to the VC.");
            return;
        }

        var searchQuery = await node.Rest.GetTracksAsync(query);
        if (searchQuery.LoadResultType == LavalinkLoadResultType.NoMatches ||
            searchQuery.LoadResultType == LavalinkLoadResultType.LoadFailed)
        {
            await ctx.RespondAsync($"Failed to find music with query: {query}");
            return;
        }

        var musicTracks = searchQuery.Tracks;

        DiscordEmoji[] emojis =
        {
            DiscordEmoji.FromName(ctx.Client, ":one:", false), DiscordEmoji.FromName(ctx.Client, ":two:", false),
            DiscordEmoji.FromName(ctx.Client, ":three:", false), DiscordEmoji.FromName(ctx.Client, ":four:", false),
            DiscordEmoji.FromName(ctx.Client, ":five:", false)
        };

        var descriptionText = emojis[0] + " | " + musicTracks.ElementAt(0).Title + "\n" + emojis[1] + " | " +
                              musicTracks.ElementAt(1).Title + "\n" + emojis[2] + " | " +
                              musicTracks.ElementAt(2).Title + "\n" + emojis[3] + " | " +
                              musicTracks.ElementAt(3).Title + "\n" + emojis[4] + " | " +
                              musicTracks.ElementAt(4).Title;

        var embedMessage = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Orange,
            Title = "Which one do you want me to play?",
            Description = descriptionText
        };

        var sentMessage = await ctx.Channel.SendMessageAsync(embedMessage);
        foreach (var emoji in emojis) await sentMessage.CreateReactionAsync(emoji);

        var trackIndex = -1;
        var interactionMessage =
            await interactivity.WaitForReactionAsync(message => message.Message.Id == sentMessage.Id);

        if (interactionMessage.Result.Message.Id == sentMessage.Id)
        {
            if (interactionMessage.Result.Emoji == emojis[0])
                trackIndex = 0;
            else if (interactionMessage.Result.Emoji == emojis[1])
                trackIndex = 1;
            else if (interactionMessage.Result.Emoji == emojis[2])
                trackIndex = 2;
            else if (interactionMessage.Result.Emoji == emojis[3])
                trackIndex = 3;
            else if (interactionMessage.Result.Emoji == emojis[4]) trackIndex = 4;
        }

        if (trackIndex != -1)
        {
            foreach (var t in OnPlaybackFinishHandlers)
                if (!t.Value.IsAlive)
                    OnPlaybackFinishHandlers.Remove(t.Key);

            if (!OnPlaybackFinishHandlers.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var thread))
                PlaybackFinishedManager(conn);

            if (conn.CurrentState.CurrentTrack == null)
            {
                await conn.PlayAsync(musicTracks.ElementAt(trackIndex));

                var musicDescription = $"Now Playing: {musicTracks.ElementAt(trackIndex).Title} \n" +
                                       $"Author: {musicTracks.ElementAt(trackIndex).Author} \n" +
                                       $"URL: {musicTracks.ElementAt(trackIndex).Uri}";
                var nowPlayingEmbed = new DiscordEmbedBuilder
                {
                    Color = DiscordColor.Green,
                    Title = $"Successfully joined channel {userVc.Name} and playing music",
                    Description = musicDescription
                };

                await ctx.Channel.SendMessageAsync(nowPlayingEmbed);
            }
            else
            {
                if (!musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
                    musicQueue.Add(ctx.Member.VoiceState.Guild.Id, new Queue<LavalinkTrack>());
                musicQueue[ctx.Member.VoiceState.Guild.Id].Enqueue(musicTracks.ElementAt(trackIndex));
                await ctx.RespondAsync($"Added to queue: {musicTracks.ElementAt(trackIndex).Title}");
            }
        }
    }

    [Command("pause")]
    public async Task PauseMusic(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            await ctx.RespondAsync("No tracks are playing!");
            return;
        }

        //Pausing the bot

        await conn.PauseAsync();

        var pausedEmbed = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Yellow,
            Title = "Track Paused!!"
        };

        await ctx.Channel.SendMessageAsync(pausedEmbed);

    }

    [Command("seek")]
    public async Task SeekMusic(CommandContext ctx, int seconds)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            await ctx.RespondAsync("No tracks are playing!");
            return;
        }

        //Seeking a position in the track

        await conn.SeekAsync(TimeSpan.FromSeconds(seconds));

        var seekEmbed = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Purple,
            Title = $"Track Seeked To Second: {seconds}"
        };

        await ctx.Channel.SendMessageAsync(seekEmbed);
    }

    [Command("resume")]
    public async Task ResumeMusic(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            await ctx.RespondAsync("No tracks are playing!");
            return;
        }

        //Resuming the track

        await conn.ResumeAsync();

        var resumedEmbed = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Green,
            Title = "Resumed"
        };

        await ctx.Channel.SendMessageAsync(resumedEmbed);
    }

    [Command("skip")]
    [Aliases("s")]
    public async Task Skip(CommandContext ctx)
    {
        //Controls
        if (ctx.Member.VoiceState == null || ctx.Member.VoiceState.Channel == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!ctx.Client.GetLavalink().ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (ctx.Member.VoiceState.Channel.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var lava = ctx.Client.GetLavalink();
        var conn = lava.ConnectedNodes.Values.First().GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        if (!musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
        {
            await ctx.RespondAsync("No more tracks in the queue.");
            return;
        }

        //Skipping to the next track if possible

        var nextTrack = musicQueue[ctx.Member.VoiceState.Guild.Id].Dequeue();
        if (musicQueue[ctx.Member.VoiceState.Guild.Id].Count == 0) musicQueue.Remove(ctx.Member.VoiceState.Guild.Id);

        await conn.PlayAsync(nextTrack);
        if (Repeat.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var track))
        {
            Repeat.Remove(ctx.Member.VoiceState.Guild.Id);
            await ctx.RespondAsync($"Now playing: {nextTrack.Title}\nDisabled repeat mode.");
        }
        else
        {
            await ctx.RespondAsync($"Now playing: {nextTrack.Title}");
        }
    }

    [Command("stop")]
    [Aliases("leave", "quit", "exit")]
    public async Task StopMusic(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        //Stopping the bot, resetting every local variable, and leaving the VC

        musicQueue.Remove(ctx.Member.VoiceState.Guild.Id);

        await conn.StopAsync();
        await conn.DisconnectAsync();

        var stopEmbed = new DiscordEmbedBuilder
        {
            Color = DiscordColor.Red,
            Title = "Stopped the Track",
            Description = "Successfully disconnected from the VC"
        };

        await ctx.Channel.SendMessageAsync(stopEmbed);
    }

    [Command("join")]
    [Aliases("j")]
    public async Task JoinChannel(CommandContext ctx)
    {
        DiscordChannel userVc;
        if (ctx.Member?.VoiceState != null)
        {
            userVc = ctx.Member.VoiceState.Channel;
        }
        else
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (userVc == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVc.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();

        if (node.GetGuildConnection(ctx.Member.VoiceState.Guild) != null && userVc.Id == node.GetGuildConnection(ctx.Member.VoiceState.Guild).Channel.Id)
        {
            await ctx.RespondAsync("Already in the channel.");
            return;
        }

        await node.ConnectAsync(userVc);
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Failed to connect to the VC.");
            return;
        }

        //Joining to the VC where the user is

        ReconnectionInProgress.Add(userVc.Guild.Id);
        LavalinkTrack? track = null;
        TimeSpan? ts = null;
        if (conn.CurrentState.CurrentTrack != null)
        {
            track = conn.CurrentState.CurrentTrack;
            ts = conn.CurrentState.PlaybackPosition;
        }

        if (conn.IsConnected) await conn.DisconnectAsync();
        await node.ConnectAsync(userVc);
        conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        while (OnPlaybackFinishHandlers.TryGetValue(ctx.Guild.Id, out var value)) //Loops until PlaybackFinished Thread removes itself from the dictionary
        {
            Thread.Sleep(100);
        }

        if (track != null && ts != null)
        {
            await conn.PlayAsync(track);
            Thread.Sleep(500);
            await conn.SeekAsync(ts.Value);
        }

        ReconnectionInProgress.RemoveAll(obj => obj == userVc.Guild.Id);
        PlaybackFinishedManager(conn);
        await ctx.Channel.SendMessageAsync(new DiscordEmbedBuilder { Title = $"Successfully joined {userVc.Name}", Color = DiscordColor.Blue });
    }

    [Command("volume")]
    [Aliases("vol")]
    public async Task ChangeVolume(CommandContext ctx, int volume)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        //Reformatting the volume to a number between 0 and 100, and change the bot's volume

        if (volume > 100) volume = 100;
        if (volume < 0) volume = 0;

        await conn.SetVolumeAsync(volume);

        await ctx.RespondAsync($"Volume changed to {volume}%");
    }

    [Command("repeat")]
    [Aliases("r")]
    public async Task RepeatTrack(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        var node = lavalinkInstance.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            await ctx.RespondAsync("Bot is not connected to a VC.");
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            await ctx.RespondAsync("No tracks are playing!");
            return;
        }

        //Enabling repeat mode or disabling it if it's already activated

        if (!Repeat.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var value))
        {
            Repeat.Add(ctx.Member.VoiceState.Guild.Id, conn.CurrentState.CurrentTrack);
            await ctx.RespondAsync("Enabled repeat mode for " + conn.CurrentState.CurrentTrack.Title);
        }
        else if (Repeat.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var track))
        {
            if (conn.CurrentState.CurrentTrack.Identifier == track.Identifier)
            {
                Repeat.Remove(ctx.Member.VoiceState.Guild.Id);
                await ctx.RespondAsync("Disabled repeat mode.");
            }
            else
            {
                Repeat[ctx.Member.VoiceState.Guild.Id] = conn.CurrentState.CurrentTrack;
                await ctx.RespondAsync("Enabled repeat mode for " + conn.CurrentState.CurrentTrack.Title);
            }
        }
    }

    [Command("queue")]
    [Aliases("info", "q", "i")]

    public async Task QueueInfo(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        //Returning the queue information to the user

        if (musicQueue.TryGetValue(ctx.Member.VoiceState.Guild.Id, out var queue))
        {
            string embedDesc = "";
            int counter = 0;
            foreach (LavalinkTrack i in musicQueue[ctx.Member.VoiceState.Guild.Id])
            {
                if (counter % 2 == 0)
                {
                    embedDesc += $"\n{i.Title} - {i.Length.ToString()}";
                }
                else
                {
                    embedDesc += $"\n**{i.Title} - {i.Length.ToString()}**";
                }
                counter++;
            }
            await ctx.Channel.SendMessageAsync(new DiscordEmbedBuilder
            {
                Title = "Next Songs",
                Description = embedDesc,
                Color = DiscordColor.Turquoise
            });
        }
        else
        {
            await ctx.RespondAsync("No more tracks in the queue.");
        }
    }

    [Command("shuffle")]
    [Aliases("random", "rng")]
    public async Task Shuffle(CommandContext ctx)
    {
        //Controls
        if (ctx.Member.VoiceState == null || ctx.Member.VoiceState.Channel == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!ctx.Client.GetLavalink().ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (ctx.Member.VoiceState.Channel.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        //Shuffling the queue

        List<LavalinkTrack> list = new List<LavalinkTrack>(musicQueue[ctx.Member.VoiceState.Guild.Id].ToArray());
        List<int> indexes = new List<int>();
        List<LavalinkTrack> shuffled = new List<LavalinkTrack>();
        Queue<LavalinkTrack> newQueue = new Queue<LavalinkTrack>();
        int currentIndex;
        for (int i = 0; i <= list.Count - 1; i++)
        {
            indexes.Add(i);
        }

        while (indexes.Count > 1)
        {
            currentIndex = (int)(Random.Shared.NextSingle() * indexes.Count);
            shuffled.Add(list.ElementAt(indexes.ElementAt(currentIndex)));
            indexes.RemoveAt(currentIndex);
        }
        shuffled.Insert((int)(Random.Shared.NextSingle() * list.Count), list.ElementAt(indexes.ElementAt(0)));

        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            newQueue.Enqueue(shuffled[i]);
        }

        musicQueue.Remove(ctx.Member.VoiceState.Guild.Id);
        musicQueue.Add(ctx.Member.VoiceState.Guild.Id, newQueue);

        await ctx.RespondAsync("The queue has been shuffled.");
    }

    [Command("ping")]
    public async Task PingCommand(CommandContext ctx)
    {
        var userVC = ctx.Member.VoiceState.Channel;
        var lavalinkInstance = ctx.Client.GetLavalink();

        //PRE-EXECUTION CHECKS
        if (ctx.Member.VoiceState == null || userVC == null)
        {
            await ctx.RespondAsync("Please enter a VC!");
            return;
        }

        if (!lavalinkInstance.ConnectedNodes.Any())
        {
            await ctx.RespondAsync("Connection is not Established!");
            return;
        }

        if (userVC.Type != ChannelType.Voice)
        {
            await ctx.RespondAsync("Please enter a valid VC!");
            return;
        }

        //Returning the queue information to the user

        var ping = ctx.Client.Ping;
        await ctx.RespondAsync($"Latency is {ping}ms.");
    }

    private void PlaybackFinishedManager(LavalinkGuildConnection connection)
    {
        var t = new Thread(async () =>
        {
            ulong id = connection.Guild.Id;
            while (connection.IsConnected && !ReconnectionInProgress.Contains(id))
            {
                try
                {
                    if (connection.CurrentState.CurrentTrack != null)
                    {
                        continue;
                    }

                    if (Repeat.TryGetValue(connection.Guild.Id, out var track))
                    {
                        await connection.PlayAsync(track);
                        continue;
                    }

                    if (musicQueue.TryGetValue(id, out var value))
                    {
                        var nextTrack = value.Dequeue();
                        await connection.PlayAsync(nextTrack);
                        if (value.Count == 0) musicQueue.Remove(id);
                    }

                    Thread.Sleep(500);
                }
                catch (Exception e)
                {
#if DEBUG
                    Console.WriteLine("////////////////////////////////////\n{0}\n{1}", e.Message, id);
#else
                    await using StreamWriter outputFile =
                    new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "ErrorLog.txt"), append: true);
                    outputFile.WriteLine("{0}/////{1} - {2}: {3}", DateTime.Now, id, e.GetBaseException(), e.Message);
#endif
                }
            }

            OnPlaybackFinishHandlers.Remove(id);
        });
        OnPlaybackFinishHandlers.Add(connection.Guild.Id, t);
        t.Start();
    }
}
