﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using JuddBot.Modules.Standard;
using JuddBot.Modules.Splatoon;

namespace JuddBot
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient socketClient;
        private DiscordRestClient restClient;
        private Config config;
        private CommandHandler handler;
        //private UptimeService uptime;
        private RankedService rankedService;
        private ServiceProvider map;
        //private IServiceProvider services;
        //private readonly IDependencyMap map = new DependencyMap();
        //private readonly CommandService commands = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });
        private ulong updateChannel = 0;

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true
            });
            socketClient.Log += Log;

            restClient = new DiscordRestClient(new DiscordRestConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            restClient.Log += Log;

            if (File.Exists("./update"))
            {
                var temp = File.ReadAllText("./update");
                ulong.TryParse(temp, out updateChannel);
                File.Delete("./update");
                Console.WriteLine($"Found an update file! It contained [{temp}] and we got [{updateChannel}] from it!");
            }

            config = Config.Load();
            //uptime = new UptimeService();
            rankedService = new RankedService(socketClient, config, restClient);

            //var map = new DependencyMap();
            map = new ServiceCollection().AddSingleton(socketClient).AddSingleton(config).AddSingleton(rankedService)/*.AddSingleton(uptime)*/.BuildServiceProvider();

            //await ConfigureServicesAsync(map);

            await socketClient.LoginAsync(TokenType.Bot, config.Token);
            await socketClient.StartAsync();

            await restClient.LoginAsync(TokenType.Bot, config.Token);

            if (File.Exists("./deadlock"))
            {
                Console.WriteLine("We're recovering from a deadlock.");
                File.Delete("./deadlock");
                foreach (var u in config.OwnerIds)
                {
                    (await restClient.GetUserAsync(u))?
                        .SendMessageAsync($"I recovered from a deadlock.\n`{DateTime.Now.ToShortDateString()}` `{DateTime.Now.ToLongTimeString()}`");
                }
            }

            socketClient.GuildAvailable += Client_GuildAvailable;
            //client.GuildMemberUpdated += Client_UserUpdated;
            // memes

            //await uptime.Install(map);

            socketClient.UserJoined += Client_UserJoined;

            handler = new CommandHandler();
            await handler.Install(map);

            //Task.Run(async () =>
            //{
            //    await Task.Delay(1000 * 60); // wait a minute before downloading to ensure we have access to the server
            //    await client.DownloadUsersAsync(new IGuild[] { client.GetGuild(110373943822540800) });
            //    var role = client.GetGuild(110373943822540800).GetRole(110374777914417152);

            //    while (true)
            //    {
            //        foreach (var u in client.GetGuild(110373943822540800).Users.Where(x => x?.IsBot == true))
            //        {
            //            if (!u.Roles.Contains(role))
            //            {
            //                await u.AddRoleAsync(role);
            //            }
            //        }

            //        await Task.Delay(1000 * 60 * 30); // Wait 30 minutes
            //    }
            //});

            //await client.CurrentUser.ModifyAsync(x => x.Avatar = new Image(File.OpenRead("Minitori.png")));

            await Task.Delay(-1);
        }

        //private async Task Client_UserUpdated(SocketGuildUser before, SocketGuildUser after)
        //{
        //    if (((SocketGuildUser)before).Guild.Id != 110373943822540800)
        //        return;

        //    if (before.Id == 190544080164487168 && ((SocketGuildUser)before).Roles.Count() != ((SocketGuildUser)after).Roles.Count())
        //    {
        //        var testMute = ((SocketGuildUser)after).Guild.GetRole(132106771975110656);
        //        var superMute = ((SocketGuildUser)after).Guild.GetRole(132106637614776320);

        //        if (((SocketGuildUser)after).Roles.Contains(superMute) || ((SocketGuildUser)after).Roles.Contains(testMute))
        //        {
        //            await Task.Delay(200);
        //            await ((SocketGuildUser)after).RemoveRolesAsync(new IRole[] { testMute, superMute });
        //        }
        //    }
        //}

        private async Task Client_UserJoined(SocketGuildUser user)
        {
            if (user.Username.ToLower().Contains("platinbots") || user.Username.ToLower().Contains("botsplat") ||
                user.Username.ToLower().Contains("discord.gg/") ||
                (user.Username.ToLower().Contains("twitch") && user.Username.ToLower().Contains("tv") && user.Username.ToLower().Contains("binzy")) ||
                (user.Username.ToLower().Contains("twitter") && user.Username.ToLower().Contains(".com") && user.Username.ToLower().Contains("senseibin"))
                )
            {
                await user.Guild.AddBanAsync(user.Id, reason: "Userbot/Adbot");
                return;
            }

            //if (user.Guild.Id == 110373943822540800 && user.IsBot)
            //{
            //    await Task.Delay(2500);
            //    var roles = new IRole[] { user.Guild.GetRole(318748748010487808), user.Guild.GetRole(110374777914417152) };
            //    await user.AddRolesAsync(roles);
            //}
        }

        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (updateChannel != 0 && guild.GetTextChannel(updateChannel) != null)
            {
                await Task.Delay(3000); // wait 3 seconds just to ensure we can actually send it. this might not do anything.
                await guild.GetTextChannel(updateChannel).SendMessageAsync("aaaaaand we're back.");
                updateChannel = 0;
            }
        }

        private async Task SocketClient_Disconnected(Exception ex)
        {
            // If we disconnect, wait 3 minutes and see if we regained the connection.
            // If we did, great, exit out and continue. If not, check again 3 minutes later
            // just to be safe, and restart to exit a deadlock.
            var task = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    await Task.Delay(1000 * 60 * 3);

                    if (socketClient.ConnectionState == ConnectionState.Connected)
                        break;
                    else if (i == 1)
                    {
                        File.Create("./deadlock");
                        Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
                    }
                }
            });
        }

        private Task Log(LogMessage msg)
        {
            //Console.WriteLine(msg.ToString());

            //Color
            ConsoleColor color;
            switch (msg.Severity)
            {
                case LogSeverity.Error: color = ConsoleColor.Red; break;
                case LogSeverity.Warning: color = ConsoleColor.Yellow; break;
                case LogSeverity.Info: color = ConsoleColor.White; break;
                case LogSeverity.Verbose: color = ConsoleColor.Gray; break;
                case LogSeverity.Debug: default: color = ConsoleColor.DarkGray; break;
            }

            //Exception
            string exMessage;
            Exception ex = msg.Exception;
            if (ex != null)
            {
                while (ex is AggregateException && ex.InnerException != null)
                    ex = ex.InnerException;
                exMessage = $"{ex.Message}";
                if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                    exMessage += $"\n{ex.StackTrace}";
            }
            else
                exMessage = null;

            //Source
            string sourceName = msg.Source?.ToString();

            //Text
            string text;
            if (msg.Message == null)
            {
                text = exMessage ?? "";
                exMessage = null;
            }
            else
                text = msg.Message;

            //if (text.Contains("GUILD_UPDATE: ") && text.Contains("UTC"))
            //    return Task.CompletedTask;
            //else if (text.StartsWith("CHANNEL_UPDATE: "))
            //    return Task.CompletedTask;

            if (sourceName == "Command")
                color = ConsoleColor.Cyan;
            else if (sourceName == "<<Message")
                color = ConsoleColor.Green;
            else if (sourceName == ">>Message")
                return Task.CompletedTask;

            //Build message
            StringBuilder builder = new StringBuilder(text.Length + (sourceName?.Length ?? 0) + (exMessage?.Length ?? 0) + 5);
            if (sourceName != null)
            {
                builder.Append('[');
                builder.Append(sourceName);
                builder.Append("] ");
            }
            builder.Append($"[{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}] ");
            for (int i = 0; i < text.Length; i++)
            {
                //Strip control chars
                char c = text[i];
                if (c == '\n' || !char.IsControl(c) || c != (char)8226)
                    builder.Append(c);
            }
            if (exMessage != null)
            {
                builder.Append(": ");
                builder.Append(exMessage);
            }

            text = builder.ToString();
            //if (msg.Severity <= LogSeverity.Info)
            //{
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            //}
#if DEBUG
            System.Diagnostics.Debug.WriteLine(text);
#endif



            return Task.CompletedTask;
        }
    }
}
