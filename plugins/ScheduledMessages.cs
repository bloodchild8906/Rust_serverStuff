//#define ScheduledMessages_DEBUG
// Uncomment above to enable debug statements. Will only be useful for developers or when debugging a problem.

using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;

#if RUST
using Oxide.Core;
using Oxide.Game.Rust.Libraries;
#endif

namespace Oxide.Plugins
{
    [Info("Scheduled Messages", "gunman435", "1.2.0")]
    [Description("Allows the creation of custom messages to broadcast to players.")]
    class ScheduledMessages : CovalencePlugin
    {
        #region Fields
        // Timer for ticking scheduled messages.
        private Timer messageTimer;
        // Random number generator for getting next scheduled message.
        private System.Random rand;
        // Integer for tracking the next sequential message.
        private int nextMessage = 0;
#if RUST
        // Include server library for Rust so we can use AvatarIDs.
        protected Server Server = Interface.Oxide.GetLibrary<Server>();
#endif
        #endregion

        #region Config
        // Config variable.
        private Configuration config;

        // Class for holding config elements.
        class Configuration
        {
            // Holds the custom messages.
            [JsonProperty(PropertyName = "Scheduled Messages")]
            public List<string> scheduledMessages;

            // The time in seconds at which a message is broadcasted.
            [JsonProperty(PropertyName = "Scheduled Messages Interval")]
            public float scheduledMesssagesInterval;

            // The SteamID64 of the Avatar to use in broadcasted messages.
            [JsonProperty(PropertyName = "Scheduled Messages Avatar ID")]
            public ulong scheduledMessagesAvatarID;

            // Whether the next scheduled message should be decided randomly or sequentially.
            [JsonProperty(PropertyName = "Scheduled Messages Randomizer")]
            public bool scheduledMessagesRandom;
        }

        /// <summary>
        /// Loads the plugin configuration.
        /// </summary>
        protected override void LoadConfig()
        {
#if ScheduledMessages_DEBUG
            Puts("Started loading config...");
#endif
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();

            // No existing config found, load default one and save it.
            if (config == null)
            {
#if ScheduledMessages_DEBUG
                Puts("Fresh install or missing config, creating default config and saving it...");
#endif
                LoadDefaultConfig();
                SaveConfig();
            }

#if ScheduledMessages_DEBUG
            Puts("Loaded config.");
#endif
        }

        /// <summary>
        /// Loads the default plugin configuration.
        /// </summary>
        protected override void LoadDefaultConfig() => config = new Configuration
        {
            scheduledMessages = new List<string>
            {
                "This is a scheduled message, this plugin was originally intended for a community I run. But I thought making it public would serve a better purpose. I hope you find the plugin useful!"
            },
            scheduledMesssagesInterval = 30f,
            scheduledMessagesAvatarID = 0,
            scheduledMessagesRandom = true
        };

        /// <summary>
        /// Saves the plugin configuration.
        /// </summary>
        protected override void SaveConfig() => Config.WriteObject(config, true);
        #endregion

        #region Language/Localization
        /// <summary>
        /// Loads the plugin localization messages.
        /// </summary>
        protected override void LoadDefaultMessages()
        {
#if ScheduledMessages_DEBUG
            Puts("Started registering languages...");
#endif

            // English translation.
            lang.RegisterMessages(new Dictionary<string, string>
            {
                // For when someone is missing permission to use a command.
                ["MissingPermission"] = "You do not have permission to use the '{0}' command!",

                // When a message gets added.
                ["ScheduledMessagesAdded"] = "The message '{0}' has been added!",
                // When a message gets removed.
                ["ScheduledMessagesRemoved"] = "The message at position {0} has been removed!",
                // When a message gets edited.
                ["ScheduledMessagesEdited"] = "The message at position {0} has been changed to '{1}'",
                // When all current messages are shown.
                ["ScheduledMessagesShow"] = "These are the current messages:\n{0}",
                // When the avatar is changed.
                ["ScheduledMessagesAvatarChanged"] = "Scheduled messages avatar has been changed to {0}!",
                // When the message interval is changed.
                ["ScheduledMessagesIntervalChanged"] = "Scheduled messages interval has been changed to {0} seconds!",
                // When scheduled messages are turned on.
                ["ScheduledMessagesOn"] = "Scheduled messages have been enabled!",
                // When scheduled messages are already turned on and someone is trying to turn them on.
                ["ScheduledMessagesAlreadyOn"] = "Scheduled messages are already enabled!",
                // When scheduled messages are turned off.
                ["ScheduledMessagesOff"] = "Scheduled messages have been disabled!",
                // When scheduled messages are already turned off and someone is trying to turn them off.
                ["ScheduledMessagesAlreadyOff"] = "Scheduled messages are already disabled!",
                // When scheduled messages are turned off due to no messages left to use.
                ["ScheduledMessagesOff2"] = "Scheduled messages have been disabled due to no messages being registered.",
                // When the scheduled messages randomizer is turned on.
                ["ScheduledMessagesRandomOn"] = "Scheduled messages will now be in a random order.",
                // When the scheduled messages randomizer is already on and someone is trying to turn it on.
                ["ScheduledMessagesRandomAlreadyOn"] = "Scheduled messages are already in random order!",
                // When the scheduled messages randomizer is turned off.
                ["ScheduledMessagesRandomOff"] = "Scheduled messages will now be shown in order.",
                // When the scheduled messages randomizer is already off and someone is trying to turn it off.
                ["ScheduledMessagesRandomAlreadyOff"] = "Scheduled messages are already in order!",

                // Generic help message for when no recognizable sub-command is used.
                ["ScheduledMessagesHelp"] = "Schedules messages are currently {0}. These are the commands available:\n{1}",
                // Usage text for add command.
                ["ScheduledMessagesAddUsage"] = "Usage: <scheduledmessages/smsg> <add/a> <message>",
                // Usage text for remove command.
                ["ScheduledMessagesRemoveUsage"] = "Usage: <scheduledmessages/smsg> <remove/r> <position>",
                // Usage text for edit command.
                ["ScheduledMessagesEditUsage"] = "Usage: <scheduledmessages/smsg> <edit/e> <position> <message>",
                // Usage text for show command.
                ["ScheduledMessagesShowUsage"] = "Usage: <scheduledmessages/smsg> <show/s>",
                // Usage text for set avatar command.
                ["ScheduledMessagesSetAvatarUsage"] = "Usage: <scheduledmessages/smsg> <setavatar/sa> <steamid64>",
                // Usage text for set interval command.
                ["ScheduledMessagesSetIntervalUsage"] = "Usage: <scheduledmessages/smsg> <setinterval/si> <seconds>",
                // Usage text for on command.
                ["ScheduledMessagesOnUsage"] = "Usage: <scheduledmessages/smsg> <on>",
                // Usage text for off command.
                ["ScheduledMessagesOffUsage"] = "Usage: <scheduledmessages/smsg> <off>",
                // Usage text for random command.
                ["ScheduledMessagesRandomUsage"] = "Usage: <scheduledmessages/smsg> <random> <on/off>",

                // Direct translation for "on", used in help message.
                ["on"] = "on",
                // Direct translation for "off", used in help message.
                ["off"] = "off"
            }, this);

            // Add other languages here.

#if ScheduledMessages_DEBUG
            Puts("Finished registering languages.");
#endif
        }
        #endregion

        #region Plugin Events
        /// <summary>
        /// Called when the server has finished startup or this plugin has been hotloaded.
        /// </summary>
        /// <param name="initial">Whether this is being called on server finishing startup or not.</param>
        void OnServerInitialized(bool initial)
        {
#if ScheduledMessages_DEBUG
            Puts("Loading plugin...");
#endif
            // Create random number generator.
            rand = new System.Random();

            // Start scheduled messages if we have any.
            if (config.scheduledMessages.Count != 0)
                StartScheduledMessages();

            // Register the permissions we need.
            permission.RegisterPermission("scheduledmessages.add", this);
            permission.RegisterPermission("scheduledmessages.remove", this);
            permission.RegisterPermission("scheduledmessages.edit", this);
            permission.RegisterPermission("scheduledmessages.show", this);
            permission.RegisterPermission("scheduledmessages.setavatar", this);
            permission.RegisterPermission("scheduledmessages.setinterval", this);
            permission.RegisterPermission("scheduledmessages.on", this);
            permission.RegisterPermission("scheduledmessages.off", this);
            permission.RegisterPermission("scheduledmessages.random", this);

#if ScheduledMessages_DEBUG
            Puts("Finished loading plugin.");
#endif
        }

        /// <summary>
        /// Called when the plugin is being unloaded.
        /// </summary>
        void Unload()
        {
#if ScheduledMessages_DEBUG
            Puts("Started unloading plugin...");
#endif
            // Cleanup after ourselves.
            StopScheduledMessages();

#if ScheduledMessages_DEBUG
            Puts("Finished unloading plugin.");
#endif
        }
        #endregion

        #region Scheduled Messages
        /// <summary>
        /// Starts the scheduled messages timer. If it has already started, destroy it and start again.
        /// </summary>
        private void StartScheduledMessages()
        {
#if ScheduledMessages_DEBUG
            Puts("Starting/Restarting messages timer...");
#endif

            // Create the message timer.
            if (messageTimer == null || messageTimer.Destroyed)
                messageTimer = timer.Every(config.scheduledMesssagesInterval, NextScheduledMessage);
            // Timer already exists, destroy it and recreate it next frame.
            else if (!messageTimer.Destroyed)
            {
                messageTimer.Destroy();
                NextFrame(() =>
                {
                    StartScheduledMessages();
                });
            }
        }

        /// <summary>
        /// Stops the scheduled message timer if it exists.
        /// </summary>
        private void StopScheduledMessages()
        {
#if ScheduledMessages_DEBUG
            Puts("Stopping messages timer...");
#endif

            // Only destroy the timer if it exists and is not destroyed already.
            if (messageTimer != null && !messageTimer.Destroyed)
                messageTimer.Destroy();
        }

        /// <summary>
        /// The callback to the timer, gets a message and broadcasts it.
        /// </summary>
        private void NextScheduledMessage()
        {
            // Bail if the timer is supposed to be destroyed.
            if (messageTimer.Destroyed)
                return;

            // Get message in the list.
            string message;
            // Get the next message randomly.
            if (config.scheduledMessagesRandom)
                message = config.scheduledMessages[rand.Next(0, config.scheduledMessages.Count)];
            // Get the next message sequentially.
            else
            {
                // Get next message index.
                nextMessage++;
                if (nextMessage >= config.scheduledMessages.Count)
                    nextMessage = 0;

                // Get next message.
                message = config.scheduledMessages[nextMessage];
            }

            // Broadcast the message.
            BroadcastMessage(message, config.scheduledMessagesAvatarID);
            // Call hook for when a scheduled message is broadcasted.
            plugins.CallHook("OnScheduledMessageBroadcasted", message, config.scheduledMessagesAvatarID);
        }

        #region Scheduled Messages Command
        /// <summary>
        /// Executes a players command for scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        [Command("scheduledmessages", "smsg"), Permission("scheduledmessages.cmd")]
        private void ScheduledMessagesCommand(IPlayer ply, string command, string[] args)
        {
#if ScheduledMessages_DEBUG
            Puts($"{ply.Name} used command.");
#endif
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.cmd"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command}"));
                return;
            }
            // If no arguments are passed then just display help section.
            else if (args.Length == 0)
            {
                HelpCommand(ply, command, args);
                return;
            }

#if ScheduledMessages_DEBUG
            Puts($"{ply.Name} passed initial checks.");
#endif

            // Boolean for whether the config has been edited during this function.
            bool configEdited;

            // Provide different functionality based on the first argument (sub-command)
            switch (args[0])
            {
                // Add command.
                case "add":
                case "a":
                    configEdited = AddCommand(ply, command, args);
                    break;
                // Remove command.
                case "remove":
                case "r":
                    configEdited = RemoveCommand(ply, command, args);
                    break;
                // Edit command.
                case "edit":
                case "e":
                    configEdited = EditCommand(ply, command, args);
                    break;
                // Show command.
                case "show":
                case "s":
                    configEdited = ShowCommand(ply, command, args);
                    break;
                // Set avatar command.
                case "setavatar":
                case "sa":
                    configEdited = SetAvatarCommand(ply, command, args);
                    break;
                // Set interval command.
                case "setinterval":
                case "si":
                    configEdited = SetIntervalCommand(ply, command, args);
                    break;
                // On command.
                case "on":
                    configEdited = OnCommand(ply, command, args);
                    break;
                // Off command.
                case "off":
                    configEdited = OffCommand(ply, command, args);
                    break;
                // Random command.
                case "random":
                    configEdited = RandomCommand(ply, command, args);
                    break;
                // Unknown/help command.
                default:
                    configEdited = HelpCommand(ply, command, args);
                    break;
            }

#if ScheduledMessages_DEBUG
            Puts($"{ply.Name} edited config: {configEdited.ToString()}");
#endif
            // If the config has been edited, save the changes.
            if (configEdited)
                SaveConfig();
        }

        /// <summary>
        /// Sub-command for adding a scheduled message.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool AddCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.add"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }

            // Build the message the user tried to use.
            string newMessage = "";
            for (int i = 1; i < args.Length; i++)
                newMessage += $" {args[i]}";
            newMessage = newMessage.Trim();

            // Check to make sure it isn't still blank.
            if (newMessage == "")
            {
                PrintToChat(ply, Lang("ScheduledMessagesAddUsage", ply.Id));
                return false;
            }
            // Add the new message.
            else
            {
                config.scheduledMessages.Add(newMessage);
                PrintToChat(ply, Lang("ScheduledMessagesAdded", ply.Id, newMessage));
                return true;
            }
        }

        /// <summary>
        /// Sub-command for removing a scheduled message.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool RemoveCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.remove"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }

            try
            {
                // Attempt to parse the index passed.
                int removeIndex = int.Parse(args[1]);
                // Attempt to remove the message at the parsed index.
                config.scheduledMessages.RemoveAt(removeIndex-1);
                // Let the player know the message at that index was removed.
                PrintToChat(ply, Lang("ScheduledMessagesRemoved", ply.Id, args[1]));

                // If no messages are left, turn off the scheduled messages and let the player know.
                if (config.scheduledMessages.Count == 0)
                {
                    StopScheduledMessages();
                    PrintToChat(ply, Lang("ScheduledMessagesOff2", ply.Id));
                }

                return true;
            }
            catch (Exception)
            {
                // Something went wrong, let the player know how to use the command.
                PrintToChat(ply, Lang("ScheduledMessagesRemoveUsage", ply.Id));
                return false;
            }
        }

        /// <summary>
        /// Sub-command for editing a scheduled message.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.<param>
        /// <param name="args">The arguments passed in the message.</param>
        /// <returns></returns>
        private bool EditCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.edit"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }

            try
            {
                // Attempt to parse the message number passed.
                int messageNumber = int.Parse(args[1]);
                // Create the new message.
                string newMessage = "";
                for (int i=2; i<args.Length; i++)
                    newMessage += $" {args[i]}";
                newMessage = newMessage.Trim();

                // Check that the new rule we got isn't empty.
                if (newMessage != "")
                {
                    // Edit the rule at the parsed index.
                    config.scheduledMessages[messageNumber - 1] = newMessage;
                    // Let the player know the message at that index+1 was edited.
                    PrintToChat(ply, Lang("ScheduledMessagesEdited", ply.Id, args[1], newMessage));

                    return true;
                }
                else
                    PrintToChat(ply, Lang("ScheduledMessagesEditUsage", ply.Id));
            }
            catch (Exception)
            {
                // Something went wrong, let the player know how to use the command.
                PrintToChat(ply, Lang("ScheduledMessagesEditUsage", ply.Id));
                return false;
            }

            return false;
        }

        /// <summary>
        /// Sub-command for showing scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool ShowCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.show"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }

            string scheduledMessages = "";
            // No messages are registered.
            if (config.scheduledMessages.Count == 0)
                scheduledMessages = "There are no scheduled messages.";
            // There are messages registered.
            else
                for (int i = 0; i < config.scheduledMessages.Count; i++)
                    scheduledMessages += $"{i+1}: {config.scheduledMessages[i]}\n";

            // Show the player all the messages that are registered.
            PrintToChat(ply, Lang("ScheduledMessagesShow", ply.Id, scheduledMessages));
            return false;
        }

        /// <summary>
        /// Sub-command for changing the broadcast avatar of scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        /// <returns></returns>
        private bool SetAvatarCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.setavatar"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }
            // Check if the argument is exactly 17 characters long (the size of all SteamID64s) and make sure it isn't the default avatarID.
            else if (args[1].Length != 17 && args[1] != "0")
            {
                PrintToChat(ply, Lang("ScheduledMessagesSetAvatarUsage", ply.Id));
                return false;
            }

            try
            {
                // Attempt to parse the new avatar.
                config.scheduledMessagesAvatarID = ulong.Parse(args[1]);
                // Let the player know the avatar has been changed.
                PrintToChat(ply, Lang("ScheduledMessagesAvatarChanged", ply.Id, args[1]));
                return true;
            }
            catch (Exception)
            {
                // Something went wrong, let the player know how to use the command.
                PrintToChat(ply, Lang("ScheduledMessagesSetAvatarUsage", ply.Id));
                return false;
            }
        }

        /// <summary>
        /// Sub-command for changing the broadcast interval of scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool SetIntervalCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.setinterval"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }

            try
            {
                // Attempt to parse the new interval.
                float newInterval = float.Parse(args[1]);
                // Check if we at least got a somewhat sensible number.
                if (newInterval > 0)
                {
                    config.scheduledMesssagesInterval = newInterval;
                    // Start the scheduled messages.
                    StartScheduledMessages();
                    // Let the player know the interval has been changed.
                    PrintToChat(ply, Lang("ScheduledMessagesIntervalChanged", ply.Id, args[1]));
                    return true;
                }
                // Didn't get a sensible number, just show help text.
                else
                {
                    PrintToChat(ply, Lang("ScheduledMessagesSetIntervalUsage", ply.Id));
                    return false;
                }
            }
            catch (Exception)
            {
                // Something went wrong, let the player know how to use the command.
                PrintToChat(ply, Lang("ScheduledMessagesSetIntervalUsage", ply.Id));
                return false;
            }
        }

        /// <summary>
        /// Sub-command for enabling scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool OnCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.on"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }
            // Check if the scheduled messages timer is already running.
            else if (IsTimerRunning())
            {
                PrintToChat(ply, Lang("ScheduledMessagesAlreadyOn", ply.Id));
                return false;
            }

            // Start the scheduled messages.
            StartScheduledMessages();
            // Let the player know this happened.
            PrintToChat(ply, Lang("ScheduledMessagesOn"));
            return false;
        }

        /// <summary>
        /// Sub-command for disabling scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool OffCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.off"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }
            // Check if the scheduled messages timer is already turned off.
            else if (!IsTimerRunning())
            {
                PrintToChat(ply, Lang("ScheduledMessagesAlreadyOff", ply.Id));
                return false;
            }

            // Stop the scheduled messages.
            StopScheduledMessages();
            // Let the player know this happened.
            PrintToChat(ply, Lang("ScheduledMessagesOff"));
            return false;
        }

        /// <summary>
        /// Sub-command for enabling/disabling randomized scheduled messages.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool RandomCommand(IPlayer ply, string command, string[] args)
        {
            // Check if the player has the specific permission.
            if (!ply.HasPermission("scheduledmessages.off"))
            {
                PrintToChat(ply, Lang("MissingPermission", ply.Id, $"{command} {args[0]}"));
                return false;
            }
            // Check if we got enough arguments.
            else if (args.Length < 2)
            {
                PrintToChat(ply, Lang("ScheduledMessagesRandomUsage", ply.Id));
                return false;
            }

            switch (args[1])
            {
                // Turn it on.
                case "on":
                case "true":
                    // Check if this setting is already turned on.
                    if (config.scheduledMessagesRandom)
                    {
                        PrintToChat(ply, Lang("ScheduledMessagesRandomAlreadyOn", ply.Id));
                        return false;
                    }

                    // Turn on the random message functionality.
                    config.scheduledMessagesRandom = true;
                    // Let the player know of the change.
                    PrintToChat(ply, Lang("ScheduledMessagesRandomOn", ply.Id));
                    return true;
                // Turn it off.
                case "off":
                case "false":
                    // Check if this setting is already turned off.
                    if (!config.scheduledMessagesRandom)
                    {
                        PrintToChat(ply, Lang("ScheduledMessagesRandomAlreadyOff", ply.Id));
                        return false;
                    }

                    // Turn off the random message functionality.
                    config.scheduledMessagesRandom = false;
                    // Let the player know of the change.
                    PrintToChat(ply, Lang("ScheduledMessagesRandomOff", ply.Id));
                    return true;
                // Unknown response.
                default:
                    // Received invalid input, let the player know how to use the command.
                    PrintToChat(ply, Lang("ScheduledMessagesRandomUsage", ply.Id));
                    return false;
            }
        }

        /// <summary>
        /// Sub-command for displaying command usage and helpful text.
        /// </summary>
        /// <param name="ply">The player executing the command.</param>
        /// <param name="command">The command used.</param>
        /// <param name="args">The arguments passed in the message.</param>
        private bool HelpCommand(IPlayer ply, string command, string[] args)
        {
            // Send the player the help text.
            PrintToChat(ply, Lang("ScheduledMessagesHelp", ply.Id,
                IsTimerRunning() ? Lang("on", ply.Id) : Lang("off", ply.Id),
                $"Add - {Lang("ScheduledMessagesAddUsage", ply.Id)}\n" +
                $"Remove - {Lang("ScheduledMessagesRemoveUsage", ply.Id)}\n" +
                $"Edit - {Lang("ScheduledMessagesEditUsage", ply.Id)}\n" +
                $"Show - {Lang("ScheduledMessagesShowUsage", ply.Id)}\n" +
                $"Set Avatar - {Lang("ScheduledMessagesSetAvatarUsage", ply.Id)}\n" +
                $"Set Interval - {Lang("ScheduledMessagesSetIntervalUsage", ply.Id)}\n" +
                $"On - {Lang("ScheduledMessagesOnUsage", ply.Id)}\n" +
                $"Off - {Lang("ScheduledMessagesOffUsage", ply.Id)}\n" +
                $"Random - {Lang("ScheduledMessagesRandomUsage", ply.Id)}"));

            return false;
        }
        #endregion
        #endregion

        #region API Functions
        /// <summary>
        /// Gets the scheduled messages currently registered within this system.
        /// </summary>
        /// <returns>A list of all the scheduled messages.</returns>
        public List<string> API_GetScheduledMessagesList() { return config.scheduledMessages; }
        /// <summary>
        /// Gets the scheduled messages currently registered within this system as an array.
        /// </summary>
        /// <returns>A list of all the scheduled messages as an array.</returns>
        public string[] API_GetScheduledMessages() { return config.scheduledMessages.ToArray(); }

        /// <summary>
        /// Broadcasts the next scheduled message. This functions exactly like the regular timer.
        /// This will call the OnScheduledMessageBroadcasted hook.
        /// </summary>
        public void API_BroadcastScheduledMessage()
        {
            NextScheduledMessage();
        }
        /// <summary>
        /// Broadcasts a scheduled message at the defined index.
        /// This will not call the OnScheduledMessageBroadcasted hook.
        /// </summary>
        /// <param name="index">The index of the message to send.</param>
        /// <returns>Whether broadcasting the message was successful or not.</returns>
        public bool API_BroadcastScheduledMessage(int index)
        {
            // Check if the index provided is valid.
            if (index < 0 || index >= config.scheduledMessages.Count)
                return false;

            // Get the desired message.
            string message = config.scheduledMessages[index];
            // Broadcast the message.
            BroadcastMessage(message, config.scheduledMessagesAvatarID);

            return true;
        }
        /// <summary>
        /// Broadcasts a scheduled message at the defined index with the defined avatar ID.
        /// This will not call the OnScheduledMessageBroadcasted hook.
        /// </summary>
        /// <param name="index">The index of the message to send.</param>
        /// <param name="avatarID">The SteamID64 of the profiles avatar to use.</param>
        /// <returns>Whether broadcasting the message was successful or not.</returns>
        public bool API_BroadcastScheduledMessage(int index, ulong avatarID=0)
        {
            // Check if the index provided is valid.
            if (index < 0 || index >= config.scheduledMessages.Count)
                return false;

            // Get the desired message.
            string message = config.scheduledMessages[index];
            // Broadcast the message.
            BroadcastMessage(message, avatarID);

            return true;
        }
        #endregion

        #region API Hooks
        /// <summary>
        /// Called when a scheduled message was broadcasted.
        /// </summary>
        /// <param name="message">The message that was broadcasted.</param>
        /// <param name="avatarID">The AvatarID that was used in the message.</param>
        private void OnScheduledMessageBroadcasted(string message, ulong avatarID) { }
        #endregion

        #region Helper Functions
        /// <summary>
        /// Helper function to get a localized string and place arguments within it.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <param name="id">The UserID.</param>
        /// <param name="args">Any arguments to pass to the formatter.</param>
        /// <returns></returns>
        private string Lang(string key, string id = null, params object[] args)
        {
#if ScheduledMessages_DEBUG
            Puts($"Localizing '{key}'");
#endif
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        /// <summary>
        /// Prints a message to a players chat.
        /// </summary>
        /// <param name="ply">The player to send the message to.</param>
        /// <param name="message">The message to send, supports formatting.</param>
        /// <param name="args">The variables to pass to string.Format if message needs formatting.</param>
        private void PrintToChat(IPlayer ply, string message, params object[] args)
        {
#if ScheduledMessages_DEBUG
            Puts($"Sending '{message}' to {ply.Name}...");
#endif
#if RUST
            // Use console command so we can include a different avatarID.
            ply.Command("chat.add", 2, config.scheduledMessagesAvatarID, args.Length > 0 ? string.Format(message, args) : message);
#else
            // Just use default replying if we're not in Rust.
            ply.Reply(args.Length > 0 ? string.Format(message, args) : message);
#endif
        }

        /// <summary>
        /// Helper function to broadcast a message.
        /// </summary>
        /// <param name="message">The message to broadcast.</param>
        /// <param name="avatarID">(Rust only) the SteamID64 of the avatar to use in the messages.</param>
        private void BroadcastMessage(string message, ulong avatarID=0)
        {
#if ScheduledMessages_DEBUG
            Puts($"Broadcasting '{message}'...");
#endif
#if RUST
            // Broadcast the message to all players with the provided AvatarID.
            Server.Broadcast(message, null, avatarID);
#else
            // Broadcast the message to all players.
            server.Broadcast(message);
#endif
        }

        /// <summary>
        /// Helper function for checking if the scheduled messages timer is running or not.
        /// </summary>
        /// <returns>Whether the scheduled messages timer is running or not.</returns>
        private bool IsTimerRunning() { return messageTimer != null && !messageTimer.Destroyed; }
        #endregion
    }
}
