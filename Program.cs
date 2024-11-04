using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Lavalink;
using DSharpPlus.Net;

namespace MusicBot;

public sealed class Program
{
    public static DiscordClient Client { get; private set; }
    public static CommandsNextExtension Commands { get; private set; }

    private static async Task Main(string[] args)
    {
        //Get the details of your config.json file by deserialising it
        var configJsonFile = new JSONReader();
        await configJsonFile.ReadJSON();

        //Setting up the Bot Configuration
        var discordConfig = new DiscordConfiguration
        {
            Intents = DiscordIntents.All,
#if DEBUG
            Token = "debug_token", //Debugging Bot Token (if you don't have one insert your regular bot token instead.)
#else
            Token = "default_token", //Standart Bot Token
#endif
            TokenType = TokenType.Bot,
            AutoReconnect = true
        };

        //Apply this config to our DiscordClient
        Client = new DiscordClient(discordConfig);

        //Assign OnError Event
        Client.ClientErrored += OnClientErrored;

        //Set the default timeout for Commands that use interactivity
        Client.UseInteractivity(new InteractivityConfiguration
        {
            Timeout = TimeSpan.FromMinutes(2)
        });

        //Set up the Task Handler Ready event

        Client.Ready += OnClientReady;

        //Set up the Commands Configuration

        var commandsConfig = new CommandsNextConfiguration
        {
            StringPrefixes = [configJsonFile.prefix],
            EnableMentionPrefix = true,
            EnableDms = true,
            EnableDefaultHelp = false
        };

        Commands = Client.UseCommandsNext(commandsConfig);

        //Register your commands

        Commands.RegisterCommands<BotCommands>();

        //Lavalink config

        var endpoint = new ConnectionEndpoint
        {
            Hostname = configJsonFile.hostname,
            Port = configJsonFile.port,
            Secured = configJsonFile.secure
        };

        var lavalinkConfig = new LavalinkConfiguration
        {
            Password = configJsonFile.password,
            RestEndpoint = endpoint,
            SocketEndpoint = endpoint
        };

        var lavalink = Client.UseLavalink();

        //Connect to get the Bot online

        await Client.ConnectAsync();
        await lavalink.ConnectAsync(lavalinkConfig);
        await Client.UpdateStatusAsync(new DiscordActivity("Ananın amıyla", ActivityType.Playing), UserStatus.Idle);
        await Task.Delay(-1);
    }

    private static Task OnClientReady(DiscordClient sender, ReadyEventArgs e)
    {
        return Task.CompletedTask;
    }

    private static async Task OnClientErrored(DiscordClient sender, ClientErrorEventArgs e)
    {
#if DEBUG
        Console.Beep();
        Console.Clear();
        Console.WriteLine("{0}///// {1}: {2}", DateTime.Now, e.Exception.GetBaseException(), e.Exception.Message);
#else
        await using StreamWriter outputFile =
        new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "ErrorLog.txt"), append: true);
        outputFile.WriteLine("{0}///// {1}: {2}", DateTime.Now, e.Exception.GetBaseException(), e.Exception.Message);
#endif
    }
}