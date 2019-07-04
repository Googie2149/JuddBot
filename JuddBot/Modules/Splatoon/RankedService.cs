using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using Newtonsoft.Json;
using Discord.Rest;
using System.Net;

namespace JuddBot.Modules.Splatoon
{
    public class RankedService
    {
        DiscordSocketClient socketClient;
        DiscordRestClient restClient;
        Config config;
        Dictionary<string, Schedule> eventList = new Dictionary<string, Schedule>();
        string currentEvent = "";

        public Dictionary<ulong, string> LastMap = new Dictionary<ulong, string>();

        public RankedService(DiscordSocketClient _socketClient, Config _config, DiscordRestClient _restClient)
        {
            socketClient = _socketClient;
            restClient = _restClient;
            config = _config;

            socketClient.MessageReceived += SocketClient_MessageReceived;

            if (Directory.GetFiles("schedules").Length > 0)
            {
                foreach (var file in Directory.GetFiles("schedules"))
                {
                    var temp = JsonStorage.DeserializeObjectFromFile<Schedule>(file);

                    if (temp.StartTime.AddHours(temp.RotationLength * temp.Modes.Length) > DateTimeOffset.Now)
                        eventList.Add(temp.StartTime.ToString("yyyy-MM-dd HH:mm"), temp);
                }
            }
        }

        public string DebugOutput()
        {
            StringBuilder output = new StringBuilder();

            output.Append("Files:\n```");
            foreach (var f in Directory.GetFiles("schedules"))
            {
                output.AppendLine(f);
            }
            output.Append("```\n");

            output.Append($"Active/Upcoming Events: {eventList.Count()}\n```");
            foreach (var e in eventList)
            {
                output.AppendLine($"{e.Key} | Duration: {e.Value.Duration}, RotationLength: {e.Value.RotationLength}, Rotations: {e.Value.Modes.Length}");
            }
            output.Append("```");

            return output.ToString();
        }

        public string GetRankedMode()
        {
            if (currentEvent == "")
            {
                var values = eventList.Where(x => x.Value.StartTime > DateTimeOffset.Now).OrderBy(x => x.Key).ToList();

                if (values.Count() == 0)
                {
                    currentEvent = "nop";
                    return "There are no scheduled events at this time.";
                }

                currentEvent = values.FirstOrDefault().Key;


                //return "Something broke and I don't know what you should play! Yell at the developer until they fix it!";
            }
            else if (currentEvent == "nop")
                return "There are no scheduled events at this time.";

            if (!eventList.ContainsKey(currentEvent)) // sanity check
                return "Something broke in an overlapping way! Yell at the developer until they fix it!";

            var schedule = eventList[currentEvent];

            if (schedule.StartTime.AddHours(schedule.Duration) < DateTimeOffset.Now)
            {
                // The previous event ended, move on
                eventList.Remove(currentEvent);

                var values = eventList.Where(x => x.Value.StartTime > DateTimeOffset.Now).OrderBy(x => x.Key).ToList();

                if (values.Count() == 0)
                {
                    currentEvent = "nop";
                    return "There are no scheduled events at this time.";
                }

                currentEvent = values.FirstOrDefault().Key;

                schedule = eventList[currentEvent];
            }

            TimeSpan somethingWithTimeInTheName = DateTimeOffset.Now - schedule.StartTime;

            var rotation = somethingWithTimeInTheName.Hours / schedule.RotationLength;

            switch (schedule.Modes[rotation])
            {
                case 0:
                    return "Tower Control";
                case 1:
                    return "Rainmaker";
                case 2:
                    return "Splat Zones";
                case 3:
                    return "Clam Blitz";
                default:
                    return "Something has gone horribly wrong in an entirely new way! Yell at the developer until they fix it!";
            }


            //DateTimeOffset time = DateTimeOffset.UtcNow;
            //DateTimeOffset baseDate = new DateTimeOffset(time.Year, time.Month, time.Day, 0, 0, 0, time.Offset);
            //int rotation = time.Hour % 4;
        }

        public Dictionary<ulong, DateTimeOffset> Cooldown = new Dictionary<ulong, DateTimeOffset>();


        //public TimeSpan GetNextRotationTime()
        //{
        //    var tmp = new TimeSpan();

        //    var schedule = eventList[currentEvent];

        //    TimeSpan somethingWithTimeInTheName = DateTimeOffset.Now - schedule.StartTime;

        //    var rotation = somethingWithTimeInTheName.Hours / schedule.RotationLength;

        //    return tmp;
        //}

        private async Task SocketClient_MessageReceived(SocketMessage msg)
        {
            if (msg.Author.Id == socketClient.CurrentUser.Id || msg.Author.IsBot)
                return;

            if ((msg.Channel as SocketGuildChannel) == null)
            {
                if (socketClient.GetGuild(config.MainGuild) == null || !(socketClient.GetGuild(config.MainGuild) as IGuild).Available) // Make sure we're connected to the server first
                {
                    await (socketClient.GetChannel(config.ManagerChannel) as SocketTextChannel)
                        .SendMessageAsync($"Sorry, it looks like I'm having an issue connecting to that server right now due to a Discord outage. " +
                        $"If this issue persists, please yell at the developer.");
                    return;
                }

                SocketGuildUser socketUser = socketClient.GetGuild(config.MainGuild).GetUser(msg.Author.Id);
                RestGuildUser restUser = null;

                List<ulong> roles = new List<ulong>();

                if (socketUser != null)
                    roles.AddRange(socketUser.Roles.Select(x => x.Id));
                else
                    restUser = await restClient.GetGuildUserAsync(config.MainGuild, msg.Author.Id);

                if (roles.Count() == 0 && restUser == null)
                    return;
                else if (restUser != null)
                    roles.AddRange(restUser.RoleIds);

                if (!roles.Contains(config.StaffRole))
                    return;

                // we're pretty certain we're getting a DM from a staff member, do something about it

                if (msg.Attachments.Count() > 0)
                {
                    string filename = msg.Attachments.FirstOrDefault().Filename;
                    string url = "";
                    string json = "";

                    if (filename.StartsWith("Schedule-") && filename.EndsWith(".json"))
                    {
                        url = msg.Attachments.FirstOrDefault().Url;
                    }
                    else
                    {
                        await msg.Channel.Respond("That doesn't look like a scheuld intended for me :/");
                        return;
                    }

                    if (url != "")
                    {
                        await Task.Run(async () =>
                        {
                            try
                            {
                                using (WebClient client = new WebClient())
                                {
                                    Console.WriteLine("Downloading...");

                                    json = client.DownloadString(new Uri(url));
                                    Console.WriteLine("Downloaded");
                                }
                            }
                            catch (Exception ex)
                            {
                                await msg.Channel.SendMessageAsync($"There was an error downloading that file:\n{ex.Message}");
                                string exMessage;
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

                                Console.WriteLine(exMessage);

                                return;
                            }
                        });

                        Schedule tempSchedule;

                        if (json != "")
                        {
                            try
                            {
                                tempSchedule = JsonConvert.DeserializeObject<Schedule>(json);
                            }
                            catch (Exception ex)
                            {
                                await msg.Channel.SendMessageAsync($"There was an error loading that file:\n{ex.Message}");
                                string exMessage;
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

                                Console.WriteLine(exMessage);

                                return;
                            }

                            if (tempSchedule != null)
                            {
                                eventList.Add(tempSchedule.StartTime.ToString("yyyy-MM-dd HH:mm"), tempSchedule);

                                await JsonStorage.SerializeObjectToFile(tempSchedule, $"schedules/{filename}");
                                await msg.Channel.SendMessageAsync("Added!");
                            }
                        }
                    }
                }
            }
        }
    }

    public class Schedule
    {
        public DateTimeOffset StartTime;
        public int Duration;
        public int RotationLength;
        public int[] Modes;
    }
}
