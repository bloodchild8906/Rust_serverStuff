#define DEBUG
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Database;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using UnityEngine;

using Connection = Oxide.Core.Database.Connection;

//EasyAdmin created with PluginMerge v(1.0.4.0) by MJSU @ https://github.com/dassjosh/Plugin.Merge
namespace Oxide.Plugins
{
    [Info("EasyAdmin", "Shady14u", "1.0.6")]
    [Description("")]
    public partial class EasyAdmin : RustPlugin
    {
        #region 0.EasyAdmin.cs
        [PluginReference] private Plugin BetterChatMute, Clans, Vanish;
        
        readonly List<ulong> _openContainers = new List<ulong>();
        private readonly Core.MySql.Libraries.MySql _sql = Interface.Oxide.GetLibrary<Core.MySql.Libraries.MySql>();
        private string _version = "1.0.4";
        private Connection _sqlConn;
        
        private bool IsVoiceBanned(BasePlayer player)
        {
            return _storedData.PlayerBans.Any(x =>
            x.BanTarget == player.UserIDString &&
            x.BanType == "Voice" &&
            (x.BanDuration == -1 ||
            x.BanDate.AddMinutes(double.Parse(x.BanDuration.ToString())) > DateTime.Now));
            
        }
        
        private void PerformBan(string action, bool isTest, BasePlayer targetPlayer, string message, string actionDuration,
        BasePlayer adminPlayer, string actionOwner, string targetName)
        {
            var actionName = "";
            switch (action)
            {
                case "Kick":
                actionName = _config.BanOptions.FirstOrDefault(x => x.BanType=="Kick" && x.Minutes.ToString() == actionDuration)?.BanMessage;
                
                if (!isTest)
                {
                    if (targetPlayer != null)
                    Network.Net.sv.Kick(targetPlayer.net.connection, $"{actionName} {_config.Reasons[message]}");
                }
                
                break;
                case "Ban":
                actionName = _config.BanOptions.FirstOrDefault(x => x.BanType=="Ban" && x.Minutes.ToString() == actionDuration)?.BanMessage;
                if (!isTest && targetPlayer != null)
                {
                    Network.Net.sv.Kick(targetPlayer.net.connection, $"{actionName} {_config.Reasons[message]}");
                }
                
                break;
                case "Chat":
                actionName = _config.BanOptions.FirstOrDefault(x => x.BanType=="Chat" && x.Minutes.ToString() == actionDuration)?.BanMessage;
                if (!isTest)
                {
                    if (actionDuration == "-1")
                    {
                        //Perm Mute
                        if (BetterChatMute && BetterChatMute.IsLoaded)
                        {
                            BetterChatMute.Call("API_Mute", targetPlayer?.IPlayer, adminPlayer.IPlayer,
                            _config.Reasons[message], true, true);
                        }
                        else
                        {
                            if (targetPlayer != null) targetPlayer.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, true);
                        }
                    }
                    else
                    {
                        //Timed Mute
                        if (BetterChatMute && BetterChatMute.IsLoaded)
                        {
                            BetterChatMute.Call("API_TimeMute", targetPlayer?.IPlayer, adminPlayer.IPlayer,
                            new TimeSpan(0, int.Parse(actionDuration), 0), _config.Reasons[message], true,
                            true);
                        }
                        else
                        {
                            //TODO: SETUP WAY to remove Mutes on a timer..
                            adminPlayer.ChatMessage("You need BetterChatMute to perform a timed chat mute. Permanent mutes are still available ");
                            return;
                        }
                    }
                }
                
                break;
                case "Voice":
                actionName = _config.BanOptions.FirstOrDefault(x => x.BanType=="Voice" && x.Minutes.ToString() == actionDuration)?.BanMessage;
                break;
            }
            
            
            if (!isTest)
            {
                if (_config.MySql.UseMySql)
                {
                    try
                    {
                        if(_sqlConn?.Con?.State != ConnectionState.Open) _sqlConn?.Con?.Open();
                        var insertQry = Sql.Builder.Append(
                        $"INSERT INTO `{_config.MySql.Db}`.EasyAdminBans(`AdminUser`,`BanDate`,`BanDuration`,`BanReason`,`BanTarget`,`BanText`,`BanType`,`BanTargetName`) VALUES (@0,@1,@2,@3,@4,@5,@6,@7)",
                        adminPlayer.UserIDString, DateTime.Now, actionDuration, _config.Reasons[message],
                        actionOwner, actionName, action, targetName);
                        _sql.Insert(insertQry,_sqlConn);
                    }
                    catch (Exception e)
                    {
                        PrintWarning(e.Message);
                        return;
                    }
                }
                
                _storedData.PlayerBans.Add(new PlayerBan
                {
                    BanDate = DateTime.Now,
                    BanDuration = int.Parse(actionDuration),
                    BanReason = _config.Reasons[message],
                    BanTarget = actionOwner,
                    BanType = action,
                    AdminUser = adminPlayer.UserIDString,
                    BanText = actionName,
                    BanTargetName = targetName
                });
                
                SaveData();
                
                
            }
            
            
            var msg = GetMsg(PluginMessages.BanMessage)
            .Replace("{target}", targetName).Replace("{targetId}", actionOwner).Replace("{actionName}", actionName)
            .Replace("{bannedBy}", adminPlayer.displayName).Replace("{reason}", _config.Reasons[message]);
            
            if (!_config.NotifyUsers) return;
            
            foreach (var basePlayer in BasePlayer.activePlayerList)
            {
                basePlayer.ChatMessage(msg);
            }
        }
        
        private BasePlayer GetPlayer(string userId)
        {
            foreach (var basePlayer in BasePlayer.activePlayerList.Where(basePlayer => basePlayer.UserIDString == userId))
            {
                return basePlayer;
            }
            
            return BasePlayer.sleepingPlayerList.FirstOrDefault(basePlayer => basePlayer.UserIDString == userId);
        }
        
        private void Teleport(BasePlayer player, Vector3 transformPosition)
        {
            try
            {
                player.UpdateActiveItem(new ItemId(0));
                player.EnsureDismounted();
                player.Server_CancelGesture();
                
                if (player.HasParent())
                {
                    player.SetParent(null, true, true);
                }
                
                if (player.IsConnected)
                {
                    player.EndLooting();
                }
                
                player.RemoveFromTriggers();
                player.Teleport(transformPosition);
                
                if (!player.IsConnected || Net.sv.visibility.IsInside(player.net.@group, transformPosition)) return;
                
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.SendEntityUpdate();
                
                if (IsInvisible(player)) return;
                player.UpdateNetworkGroup();
                player.SendNetworkUpdateImmediate(false);
            }
            finally
            {
                if (!IsInvisible(player))
                player.ForceUpdateTriggers();
            }
        }
        
        bool IsInvisible(BasePlayer player)
        {
            return Vanish != null && Convert.ToBoolean(Vanish?.Call("IsInvisible", player));
        }
        #endregion

        #region 1.EasyAdmin.Config.cs
        private static Configuration _config;
        
        #region
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) LoadDefaultConfig();
                SaveConfig();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                PrintWarning("Creating new config file.");
                LoadDefaultConfig();
            }
        }
        
        protected override void LoadDefaultConfig() => _config = Configuration.DefaultConfig();
        protected override void SaveConfig() => Config.WriteObject(_config);
        
        #endregion
        
        public class Configuration
        {
            [JsonProperty(PropertyName = "BackGround Color - Gui (Default 0.1 0.1 0.1 0.93)")]
            public string BackGroundColor = "0.1 0.1 0.1 0.93";
            
            [JsonProperty(PropertyName = "Ban Options")]
            public List<EasyBan> BanOptions;
            
            [JsonProperty(PropertyName = "Border Color - Gui (Default 0 0 0 0.8)")]
            public string BorderColor = "0 0 0 0.8";
            
            [JsonProperty(PropertyName = "Button Clans Anchor Max (Default 0.456 0.09)")]
            public string ButtonClansAnchorMax = "0.456 0.09";
            
            [JsonProperty(PropertyName = "Button Clans Anchor Min (Default 0.2835 0.06)")]
            public string ButtonClansAnchorMin = "0.2835 0.06";
            
            [JsonProperty(PropertyName = "Button Color - Gui (Default .11 .65 .18 1)")]
            public string ButtonColor = ".11 .65 .18 1";
            
            [JsonProperty(PropertyName = "Button Color Kick - Gui (Default 0.65 0.18 0.11 1)")]
            public string ButtonColor1 = "0.65 0.18 0.11 1";
            
            [JsonProperty(PropertyName = "Button Color Secondary - Gui (Default 0.65 0.18 .11 1)")]
            public string ButtonColor2 = "0.65 0.18 .11 1";
            
            [JsonProperty(PropertyName = "Button Color Chat - Gui (Default 0.90 0.58 0.16 1)")]
            public string ButtonColor3 = "0.90 0.58 0.16 1";
            
            [JsonProperty(PropertyName = "Button Color Voice - Gui (Default 0.90 0.58 0.16 1)")]
            public string ButtonColor4 = "0.90 0.58 0.16 1";
            
            [JsonProperty(PropertyName = "Log Button Anchor Max (Default 0.54 0.09)")]
            public string ButtonLogsAnchorMax = "0.54 0.09";
            
            [JsonProperty(PropertyName = "Log Button Anchor Min (Default 0.46 0.06)")]
            public string ButtonLogsAnchorMin = "0.46 0.06";
            
            [JsonProperty(PropertyName = "Button Players Anchor Max (Default 0.7165 0.09)")]
            public string ButtonPlayersAnchorMax = "0.7165 0.09";
            
            [JsonProperty(PropertyName = "Button Players Anchor Min (Default 0.543 0.06)")]
            public string ButtonPlayersAnchorMin = "0.543 0.06";
            
            [JsonProperty(PropertyName = "Button Color Sleeper - Gui (Default 0.7 0.7 0.7 1)")]
            public string ButtonSleeper = "0.7 0.7 0.7 1";
            
            [JsonProperty(PropertyName = "Detail Anchor Max (Default 0.72 0.9)")]
            public string DetailAnchorMax = "0.72 0.9";
            
            [JsonProperty(PropertyName = "Detail Anchor Min (Default 0.28 0.2)")]
            public string DetailAnchorMin = "0.28 0.2";
            
            [JsonProperty(PropertyName = "Header and Footer - Gui (Default 0.13 0.06 0.44 1)")]
            public string HeaderFooterColor = "0.13 0.06 0.44 1";
            
            [JsonProperty(PropertyName = "Inventory Background (Default 0.89 0.89 0.86 0.4)")]
            public string InventoryBackColor = "0.89 0.89 0.86 0.4";
            
            [JsonProperty(PropertyName = "MySql")] public MySql MySql;
            
            [JsonProperty(PropertyName = "Notify Users")]
            public bool NotifyUsers = true;
            
            [JsonProperty(PropertyName = "Page Back Anchor Max (Default 0.3835 0.03)")]
            public string PageBackAnchorMax = "0.3835 0.03";
            
            [JsonProperty(PropertyName = "Page Back Anchor Min - Gui (Default 0.2835 0)")]
            public string PageBackAnchorMin = "0.2835 0";
            
            [JsonProperty(PropertyName = "Page Next Anchor Max (Default 0.7165 0.03)")]
            public string PageNextAnchorMax = "0.7165 0.03";
            
            [JsonProperty(PropertyName = "Page Next Anchor Min - Gui (Default 0.6165 0)")]
            public string PageNextAnchorMin = "0.6165 0";
            
            [JsonProperty(PropertyName = "Panel Anchor Max (Default 0.75 0.95)")]
            public string PanelAnchorMax = "0.75 0.95";
            
            [JsonProperty(PropertyName = "Panel Anchor Min (Default 0.25 0.13)")]
            public string PanelAnchorMin = "0.25 0.13";
            
            [JsonProperty(PropertyName = "Reasons")]
            public Dictionary<string, string> Reasons;
            
            [JsonProperty(PropertyName = "Report - Date Format (Default MM/dd/yyyy)")]
            public string ReportDateFormat = "MM/dd/yyyy";
            
            [JsonProperty(PropertyName = "Text Color - Gui (Default 1 1 1 1)")]
            public string TextColor = "1 1 1 1";
            
            #region
            
            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    BanOptions = new List<EasyBan>
                    {
                        new EasyBan {ButtonText = "Kick {targetType}", BanType = "Kick", BanMessage = "Kicked {targetType}", Minutes = 0},
                        new EasyBan {ButtonText = "Ban for 30 Minutes", BanType = "Ban", BanMessage = "Banned for 30 Minutes", Minutes = 30},
                        new EasyBan {ButtonText = "Ban for 1 Hour", BanType = "Ban", BanMessage = "Banned for 1 Hour", Minutes = 60},
                        new EasyBan {ButtonText = "Ban for 3 Hours", BanType = "Ban", BanMessage = "Banned for 3 Hours", Minutes = 180},
                        new EasyBan {ButtonText = "Ban for 1 Day", BanType = "Ban", BanMessage = "Banned for 1 Day", Minutes = 1440},
                        new EasyBan {ButtonText = "Ban for 1 Week", BanType = "Ban", BanMessage = "Banned for 1 Week", Minutes = 10080},
                        new EasyBan {ButtonText = "Ban Permanently", BanType = "Ban", BanMessage = "Banned Permanently", Minutes = -1},
                        new EasyBan {ButtonText = "Mute in Chat for 30 Minutes", BanType = "Chat", BanMessage = "Muted in Chat for 30 Minutes", Minutes = 30},
                        new EasyBan {ButtonText = "Mute in Chat for 1 Hour", BanType = "Chat", BanMessage = "Muted in Chat for 1 Hour", Minutes = 60},
                        new EasyBan {ButtonText = "Mute in Chat for 3 Hours", BanType = "Chat", BanMessage = "Muted in Chat for 3 Hours", Minutes = 180},
                        new EasyBan {ButtonText = "Mute in Chat for 1 Day", BanType = "Chat", BanMessage = "Muted in Chat for 1 Day", Minutes = 1440},
                        new EasyBan {ButtonText = "Mute in Chat for 1 Week", BanType = "Chat", BanMessage = "Muted in Chat for 1 Week", Minutes = 10080},
                        new EasyBan {ButtonText = "Mute in Chat Permanently", BanType = "Chat", BanMessage = "Muted in Chat Permanently", Minutes = -1},
                        new EasyBan {ButtonText = "Mute in Voice for 30 Minutes", BanType = "Voice", BanMessage = "Muted in Voice for 30 Minutes", Minutes = 30},
                        new EasyBan {ButtonText = "Mute in Voice for 1 Hour", BanType = "Voice", BanMessage = "Muted in Voice for 1 Hour", Minutes = 60},
                        new EasyBan {ButtonText = "Mute in Voice for 3 Hours", BanType = "Voice", BanMessage = "Muted in Voice for 3 Hours", Minutes = 180},
                        new EasyBan {ButtonText = "Mute in Voice for 1 Day", BanType = "Voice", BanMessage = "Muted in Voice for 1 Day", Minutes = 1440},
                        new EasyBan {ButtonText = "Mute in Voice for 1 Week", BanType = "Voice", BanMessage = "Muted in Voice for 1 Week", Minutes = 10080},
                        new EasyBan {ButtonText = "Mute in Voice Permanently", BanType = "Voice", BanMessage = "Muted in Voice Permanently", Minutes = -1}
                    },
                    Reasons = new Dictionary<string, string>
                    {
                        {"Reason1", "Cheating is not allowed on the server."},
                        {"Reason2", "Racism is not allowed on the server."},
                        {"Reason3", "Sussy Game Play..."},
                        {"Reason4", "Toxic Game Play..."},
                        {"Reason5", "Griefing Players Bases is not allowed."},
                        {"Reason6", "Spamming noise in chat is not allowed."},
                        {"Reason7", "Advertising is not allowed."},
                        {"Reason8", "Not following the rules."}
                    },
                    NotifyUsers = true,
                    ReportDateFormat = "dd-MM-yyyy hh:mm",
                    MySql = new MySql()
                };
            }
            
            #endregion
        }
        #endregion

        #region 2.EasyAdmin.Localization.cs
        #region
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [PluginMessages.NoPermission] = "You do not have permission to do this action!",
                [PluginMessages.LogsTitle] = "Logs",
                [PluginMessages.PlayersTitle] = "Players",
                [PluginMessages.ClansTitle] = "Clans",
                [PluginMessages.LeftArrow] = "◀",
                [PluginMessages.RightArrow] = "▶",
                [PluginMessages.TP2Player] = "TP to Player",
                [PluginMessages.TPPlayer2ME] = "TP Player to Me",
                [PluginMessages.VoiceBanned] = "You are currently banned from using voice chat.",
                [PluginMessages.BanMessage] =
                "<color=#f99900>{target} ({targetId})</color> was <color=#ff4646>{actionName}</color> by <color=#50F0E6>{bannedBy}</color>. {reason}",
                [PluginMessages.ReportText] =
                "<color=#f99900>{banDate}:</color> <color=#ff4646>{banText}</color> by <color=#50F0E6>{bannedBy}</color> - {reason}"
            }, this);
        }
        
        #endregion
        
        #region
        
        private string GetMsg(string key, object userId = null)
        {
            return lang.GetMessage(key, this, userId?.ToString());
        }
        
        #endregion
        
        private static class PluginMessages
        {
            public const string TPPlayer2ME = "TPPlayer2ME";
            public const string TP2Player = "TP2Player";
            public const string NoPermission = "NoPermission";
            public const string PlayersTitle = "PlayersTitle";
            public const string ClansTitle = "ClansTitle";
            public const string LeftArrow = "LeftArrow";
            public const string RightArrow = "RightArrow";
            public const string VoiceBanned = "VoiceBanned";
            public const string BanMessage = "BanMessage";
            public const string ReportText = "ReportText";
            public const string LogsTitle = "LogsTitle";
        }
        #endregion

        #region 3.EasyAdmin.Permissions.cs
        #region
        
        private bool CheckPermission(BasePlayer player, string perm)
        {
            //TODO: Give Admins access by default
            return permission.UserHasPermission(player.UserIDString, perm); //|| IsAdmin(player);
        }
        
        bool IsAdmin(BasePlayer player) => player?.net?.connection != null && player.net.connection.authLevel == 2;
        
        private void LoadPermissions()
        {
            permission.RegisterPermission(PluginPermissions.EasyAdminUse, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminKick, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminBan, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminChat, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminVoice, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminDeleteReports, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminViewInventory, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminRemoveInventory, this);
            permission.RegisterPermission(PluginPermissions.EasyAdminTeleport, this);
        }
        
        #endregion
        
        private static class PluginPermissions
        {
            public const string EasyAdminUse = "easyadmin.use";
            public const string EasyAdminKick = "easyadmin.kick";
            public const string EasyAdminBan = "easyadmin.ban";
            public const string EasyAdminChat = "easyadmin.chat";
            public const string EasyAdminVoice = "easyadmin.voice";
            public const string EasyAdminDeleteReports = "easyadmin.deletereports";
            public const string EasyAdminViewInventory = "easyadmin.viewinventory";
            public const string EasyAdminRemoveInventory = "easyadmin.removeinventory";
            public const string EasyAdminTeleport = "easyadmin.teleport";
            
        }
        #endregion

        #region 4.EasyAdmin.Data.cs
        private StoredData _storedData;
        
        public class StoredData
        {
            public List<PlayerBan> PlayerBans { get; set; } = new List<PlayerBan>();
            
        }
        
        #region BoilerPlate
        private void LoadData()
        {
            try
            {
                if (!_config.MySql.UseMySql)
                {
                    _storedData = Interface.GetMod().DataFileSystem.ReadObject<StoredData>("EasyAdmin");
                }
            }
            catch (Exception e)
            {
                Puts(e.Message);
                Puts(e.StackTrace);
                _storedData = new StoredData();
            }
        }
        
        private void SaveData()
        {
            if (!_config.MySql.UseMySql)
            {
                Interface.GetMod().DataFileSystem.WriteObject("EasyAdmin", _storedData);
            }
        }
        #endregion
        #endregion

        #region 5.EasyAdmin.Hooks.cs
        private void Init()
        {
            LoadPermissions();
            LoadData();
            
        }
        
        private void OnServerSave()
        {
            SaveData();
        }
        
        private void Unload()
        {
            SaveData();
        }
        
        void OnPlayerConnected(BasePlayer player)
        {
            var recentBan = _storedData.PlayerBans.Where(x =>
            x.BanTarget == player.UserIDString &&
            x.BanType == "Ban" &&
            (x.BanDuration == -1 ||
            x.BanDate.AddMinutes(double.Parse(x.BanDuration.ToString())) > DateTime.Now))
            .OrderByDescending(x => x.BanDate.AddMinutes(x.BanDuration)).FirstOrDefault();
            if (recentBan == null) return;
            //Player is Banned
            Network.Net.sv.Kick(player.net.connection, recentBan.BanReason);
        }
        
        object OnPlayerVoice(BasePlayer player)
        {
            if (!IsVoiceBanned(player)) return null;
            if (_config.NotifyUsers)
            {
                player.ChatMessage(GetMsg(PluginMessages.VoiceBanned, player.UserIDString));
            }
            
            return true;
        }
        
        void OnServerInitialized()
        {
            if (_config.MySql.UseMySql)
            {
                _sqlConn = _sql.OpenDb(_config.MySql.Host, _config.MySql.Port,
                _config.MySql.Db, _config.MySql.User, _config.MySql.Pass, this);
                _sql.Insert(Sql.Builder.Append(
                "CREATE TABLE IF NOT EXISTS `EasyAdminBans` (`Id` int(11) NOT NULL AUTO_INCREMENT,`AdminUser` varchar(45) DEFAULT NULL," +
                "`BanDate` datetime DEFAULT NULL,`BanDuration` int(11) DEFAULT NULL,`BanReason` varchar(4000) DEFAULT NULL," +
                "`BanTarget` varchar(45) DEFAULT NULL,`BanText` varchar(100) DEFAULT NULL,`BanType` varchar(45) DEFAULT NULL," +
                "`BanTargetName` varchar(45) DEFAULT NULL, PRIMARY KEY(`Id`)) AUTO_INCREMENT = 1 DEFAULT CHARSET = utf8mb4;"),
                _sqlConn, obj =>
                {
                    if (_sqlConn?.Con?.State != ConnectionState.Open) _sqlConn?.Con?.Open();
                    var selectCommand = Sql.Builder.Append($"SELECT * FROM `{_config.MySql.Db}`.EasyAdminBans where BanDuration=-1 OR Date_Add(BanDate, INTERVAL BanDuration MINUTE)>CURRENT_DATE()");
                    
                    _sql.Query(selectCommand, _sqlConn,
                    results =>
                    {
                        if (_storedData == null) _storedData = new StoredData { PlayerBans = new List<PlayerBan>() };
                        if (_storedData.PlayerBans == null) _storedData.PlayerBans = new List<PlayerBan>();
                        if (results == null || results.Count == 0) return;
                        foreach (var result in results)
                        {
                            _storedData.PlayerBans.Add(new PlayerBan
                            {
                                BanDate = DateTime.Parse(result["BanDate"].ToString()),
                                BanDuration = int.Parse(result["BanDuration"].ToString()),
                                AdminUser = result["AdminUser"].ToString(),
                                BanTarget = result["BanTarget"].ToString(),
                                BanText = result["BanText"].ToString(),
                                BanType = result["BanType"].ToString(),
                                BanReason = result["BanReason"].ToString(),
                                BanTargetName = result["BanTargetName"].ToString(),
                            });
                        }
                    });
                });
            }
            
        }
        #endregion

        #region 6.EasyAdmin.Commands.cs
        #region
        
        [ChatCommand("ea")]
        void ChatCmdEasyAdmin(BasePlayer player, string command, string[] args)
        {
            if (!CheckPermission(player, PluginPermissions.EasyAdminUse) & !IsAdmin(player))
            {
                SendReply(player, GetMsg(PluginMessages.NoPermission, player.UserIDString));
                return;
            }
            
            if (_openContainers.Contains(player.userID))
            DestroyContainers(player, true);
            
            CreateEasyAdminMainContainer(player);
            OpenEasyAdminMainPage(player, "players", 1);
        }
        
        [ConsoleCommand("CloseEasyAdmin")]
        void ConsoleCmdCloseEasyAdmin(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null) return;
            DestroyContainers(arg.Player(), true);
        }
        
        [ConsoleCommand("DeleteReportItem")]
        void ConsoleCmdDeleteReportItem(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null || arg.Args.Length < 2 ||
            (!IsAdmin(arg.Player()) &&
            !CheckPermission(arg.Player(), PluginPermissions.EasyAdminDeleteReports))) return;
            
            var id = arg.Args[1];
            var page = arg.Args[2];
            var itemKey = $"{arg.Args[0]}{id}";
            var record = _storedData.PlayerBans
            .FirstOrDefault(x => $"{x.BanDate:ddMMyyyyhhmmss}{x.BanTarget}" == itemKey);
            if (record != null)
            {
                Puts($"{record.BanTarget} {record.BanType}");
                if (record.BanType.Contains("Chat") || record.BanType.Contains("Voice"))
                {
                    arg.Player().SendConsoleCommand("bcm.unmute", record.BanTarget);
                }
                if (_config.MySql.UseMySql)
                {
                    if (_sqlConn?.Con.State != ConnectionState.Open) _sqlConn?.Con?.Open();
                    var deleteQry = Sql.Builder.Append(
                    $"Delete from `{_config.MySql.Db}`.EasyAdminBans Where CONCAT(DATE_FORMAT(BanDate,'%d%m%Y%h%i%s') , BanTarget) = @banTarget"
                    ,new {banTarget = itemKey});
                    
                    _sql.Delete(deleteQry, _sqlConn);
                }
                _storedData.PlayerBans.Remove(record);
            }
            
            
            arg.Args = new[] {"report", id, page};
            ConsoleCmdEasyAdminSelected(arg);
        }
        
        [ConsoleCommand("EasyAdminControlSelected")]
        void ConsoleCmdEasyAdminControlSelected(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length != 3) return;
            var action = arg.Args[0];
            var actionDuration = arg.Args[1];
            var actionOwner = arg.Args[2];
            
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminUse)) return;
            EasyAdminReasonsUi(player, action, actionDuration, actionOwner);
        }
        
        [ConsoleCommand("EasyAdminOpenPage")]
        void ConsoleCmdEasyAdminOpenPage(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length != 2) return;
            var action = arg.Args[0];
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminUse)) return;
            var page = Convert.ToInt16(arg.Args[1]);
            OpenEasyAdminMainPage(player, action, page);
        }
        
        [ConsoleCommand("EasyAdminReasonSelected")]
        void ConsoleCmdEasyAdminReasonSelected(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 4) return;
            var action = arg.Args[0];
            var actionDuration = arg.Args[1];
            var actionOwner = arg.Args[2];
            var message = arg.Args[3];
            var isTest = arg.Args.Length > 4;
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!IsAdmin(player) && !CheckPermission(player, PluginPermissions.EasyAdminUse)) return;
            
            var target = "Clan";
            
            BasePlayer targetPlayer;
            if (actionOwner.Length > 14)
            {
                targetPlayer = GetPlayer(actionOwner);
                if (targetPlayer == null) return;
                
                target = targetPlayer.displayName;
                PerformBan(action, isTest, targetPlayer, message, actionDuration, player, actionOwner, target);
            }
            else
            {
                
                if (!Clans || !Clans.IsLoaded) return;
                var clan = Clans?.Call<JObject>("GetClan", actionOwner);
                if (clan == null) return;
                Puts($"{clan.GetValue("tag")}");
                PerformBan(action, isTest, null, message, actionDuration, player, actionOwner, $"{clan.GetValue("tag")}");
                foreach (var member in clan["members"])
                {
                    targetPlayer = GetPlayer(member.ToString());
                    target = targetPlayer.displayName;
                    PerformBan(action, isTest, targetPlayer, message, actionDuration, player, member.ToString(), target);
                }
            }
        }
        
        
        [ConsoleCommand("PlayerAction")]
        void PlayerAction(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length < 2) return;
            var action = arg.Args[0];
            var targetId = arg.Args[1];
            var player = arg.Connection?.player as BasePlayer;
            
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminTeleport)) return;
            var targetPlayer = GetPlayer(targetId);
            
            switch (action)
            {
                case "TP2P":
                Teleport(player, targetPlayer.transform.position);
                break;
                
                case "TPP2ME":
                Teleport(targetPlayer, player.transform.position);
                break;
                
                default:
                
                break;
            }
            
        }
        
        [ConsoleCommand("EasyAdminSelected")]
        void ConsoleCmdEasyAdminSelected(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length < 2) return;
            var action = arg.Args[0];
            var id = arg.Args[1];
            var page = 1;
            if (arg.Args.Length > 2)
            {
                page = int.Parse(arg.Args[2]);
            }
            
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminUse)) return;
            OpenEasyAdminDetailContainer(player, action, id, page);
        }
        
        [ConsoleCommand("RemoveAllInventoryItems")]
        void RemoveAllInventoryItems(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length < 1) return;
            var targetId = arg.Args[0];
            var player = arg.Connection?.player as BasePlayer;
            
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminRemoveInventory)) return;
            
            var targetPlayer = GetPlayer(targetId);
            if (targetPlayer != null)
            {
                foreach (var item in targetPlayer.inventory.containerMain.itemList)
                {
                    ItemManager.RemoveItem(item);
                }
                
                foreach (var item in targetPlayer.inventory.containerBelt.itemList)
                {
                    ItemManager.RemoveItem(item);
                }
                
                foreach (var item in targetPlayer.inventory.containerWear.itemList)
                {
                    ItemManager.RemoveItem(item);
                }
                
                ItemManager.DoRemoves();
                
                targetPlayer.inventory.loot.MarkDirty();
                targetPlayer.inventory.loot.SendImmediate();
            }
            
            OpenEasyAdminDetailContainer(player, "playerInventory", targetId, 1);
        }
        
        [ConsoleCommand("RemoveInventoryItem")]
        void ConsoleCmdRemoveInventoryItem(ConsoleSystem.Arg arg)
        {
            if (arg.Args.Length < 3) return;
            var location = arg.Args[0];
            var target = arg.Args[1];
            var slotId = arg.Args[2];
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;
            if (!CheckPermission(player, PluginPermissions.EasyAdminRemoveInventory)) return;
            
            var targetPlayer = GetPlayer(target);
            if (targetPlayer != null)
            {
                switch (location)
                {
                    case "Inventory":
                    {
                        foreach (var item in targetPlayer.inventory.containerMain.itemList
                        .Where(item => item.position.ToString() == slotId))
                        {
                            ItemManager.RemoveItem(item);
                        }
                        
                        break;
                    }
                    case "Belt":
                    {
                        foreach (var item in targetPlayer.inventory.containerBelt.itemList
                        .Where(item => (item.position + 24).ToString() == slotId))
                        {
                            ItemManager.RemoveItem(item);
                        }
                        
                        break;
                    }
                    case "Wearable":
                    {
                        foreach (var item in targetPlayer.inventory.containerWear.itemList
                        .Where(item => (item.position+40).ToString() == slotId))
                        {
                            ItemManager.RemoveItem(item);
                        }
                        
                        break;
                    }
                }
                
                ItemManager.DoRemoves();
                
                targetPlayer.inventory.loot.MarkDirty();
                targetPlayer.inventory.loot.SendImmediate();
            }
            
            OpenEasyAdminDetailContainer(player, "playerInventory", target, 1);
        }
        
        #endregion
        #endregion

        #region 7.EasyAdmin.Classes.cs
        public class EasyBan
        {
            #region
            
            public string BanMessage { get; set; }
            public string BanType { get; set; }
            public string ButtonText { get; set; }
            public int Minutes { get; set; }
            
            #endregion
        }
        
        public class MySql
        {
            public string Host = string.Empty, Db = string.Empty, User = string.Empty, Pass = string.Empty;
            public int Port = 3306;
            public bool UseMySql;
        }
        
        public class PlayerBan
        {
            #region
            
            public string AdminUser { get; set; }
            public DateTime BanDate { get; set; }
            public int BanDuration { get; set; }
            public string BanReason { get; set; }
            public string BanTarget { get; set; }
            public string BanTargetName { get; set; }
            public string BanText { get; set; }
            public string BanType { get; set; }
            
            #endregion
        }
        
        public class SlotPosition
        {
            #region
            
            public int Col { get; set; }
            public string Location { get; set; }
            public int Row { get; set; }
            
            #endregion
        }
        #endregion

        #region 9.EasyAdmin.UI.cs
        #region
        
        private float AddControlPanel(float top, CuiElementContainer elements, string panelName, string panelType,
        string id, string targetType)
        {
            top -= 0.015f;
            var bottom = top - 0.07f;
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = $"{panelType} Controls", FontSize = 14, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = $"0 {bottom}", AnchorMax = $"1 {top}"}
            }, panelName);
            
            top = bottom - 0.005f;
            bottom = top - 0.035f;
            var controls = new List<EasyBan>();
            var btnColor = _config.ButtonColor;
            
            switch (panelType)
            {
                case "Kick":
                controls = _config.BanOptions.Where(x => x.BanType == "Kick").ToList();
                btnColor = _config.ButtonColor1;
                break;
                case "Ban":
                controls = _config.BanOptions.Where(x => x.BanType == "Ban").ToList();
                btnColor = _config.ButtonColor2;
                break;
                case "Chat":
                controls = _config.BanOptions.Where(x => x.BanType == "Chat").ToList();
                btnColor = _config.ButtonColor3;
                break;
                case "Voice":
                controls = _config.BanOptions.Where(x => x.BanType == "Voice").ToList();
                btnColor = _config.ButtonColor4;
                break;
            }
            
            var offset = 0.033;
            foreach (var control in controls)
            {
                elements.Add(
                new CuiButton
                {
                    Button =
                    {
                        Command = $"EasyAdminControlSelected {panelType} {control.Minutes} {id}",
                        Color = btnColor
                    },
                    RectTransform = {AnchorMin = $"{offset} {bottom}", AnchorMax = $"{offset + .3} {top}"},
                    Text =
                    {
                        Text = $"{control.ButtonText.Replace("{targetType}", targetType)}",
                        FontSize = 11, Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    }
                }, panelName);
                
                
                if (Math.Abs(offset - 0.033) < 0.01)
                {
                    offset = 0.35;
                }
                else if (Math.Abs(offset - 0.35) < 0.01)
                {
                    offset = 0.667;
                }
                else if (Math.Abs(offset - 0.667) < .01)
                {
                    offset = 0.033;
                    top -= 0.05f;
                    bottom -= 0.05f;
                }
            }
            
            return bottom;
        }
        
        private void AddInventoryItem(CuiElementContainer elements, string panelName, string targetPlayer,
        ItemDefinition item, ulong skinId,
        int itemAmount, int itemPosition, BasePlayer player)
        {
            var pos = GetSlotPosition(itemPosition);
            var colLeft = (pos.Col * .1) + 0.2;
            var rowBottom = 0.8 - ((pos.Row * 0.1) + 0.065);
            
            switch (pos.Row)
            {
                case 4:
                rowBottom -= 0.085;
                break;
                case 5:
                colLeft = (pos.Col * .1) + 0.15;
                rowBottom -= 0.15;
                break;
            }
            
            elements.Add(
            new CuiPanel()
            {
                RectTransform =
                {
                    AnchorMin = $"{colLeft + 0.01} {rowBottom}", AnchorMax = $"{colLeft + 0.1} {rowBottom + 0.091}"
                },
                Image = {Color = $"{_config.InventoryBackColor}"}
            }, panelName);
            
            
            elements.Add(
            new CuiElement
            {
                Name = CuiHelper.GetGuid(),
                Parent = panelName,
                Components =
                {
                    new CuiImageComponent
                    {
                        ItemId = item.itemid,
                        SkinId = skinId
                    }
                    ,
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{colLeft + 0.018} {rowBottom + 0.015}",
                        AnchorMax = $"{colLeft + 0.079} {rowBottom + 0.086}"
                    }
                }
            }
            );
            
            elements.Add(
            new CuiLabel
            {
                Text = {Text = $"{itemAmount}", FontSize = 7, Align = TextAnchor.LowerRight, Color = "1 1 1 1"},
                RectTransform =
                {
                    AnchorMin = $"{colLeft + 0.025} {rowBottom + 0.002}",
                    AnchorMax = $"{colLeft + 0.095} {rowBottom + 0.022}"
                }
            }, panelName
            );
            if (CheckPermission(player, PluginPermissions.EasyAdminRemoveInventory))
            {
                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"RemoveInventoryItem {pos.Location} {targetPlayer} {itemPosition}",
                        Color = _config.ButtonColor2
                    },
                    Text = {Text = "X", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = _config.TextColor},
                    RectTransform =
                    {
                        AnchorMin = $"{colLeft + 0.08} {rowBottom + 0.07}",
                        AnchorMax = $"{colLeft + 0.1} {rowBottom + 0.091}"
                    }
                }, panelName
                );
            }
        }
        
        private void AddInventoryPlaceHolder(CuiElementContainer elements, string panelName, int itemPosition)
        {
            var pos = GetSlotPosition(itemPosition);
            var colLeft = (pos.Col * .1) + 0.2;
            var rowBottom = 0.8 - ((pos.Row * 0.1) + 0.065);
            
            if (pos.Row == 4)
            {
                rowBottom -= 0.085;
            }
            
            if (pos.Row == 5)
            {
                colLeft = (pos.Col * .1) + 0.15;
                rowBottom -= 0.15;
            }
            
            elements.Add(
            new CuiPanel
            {
                RectTransform =
                {AnchorMin = $"{colLeft + .01} {rowBottom}", AnchorMax = $"{colLeft + .1} {rowBottom + .09}"},
                Image = {Color = _config.InventoryBackColor}
            }, panelName);
        }
        
        private void ClanDetailsUi(BasePlayer player, string id, CuiElementContainer elements, string panelName)
        {
            if (!Clans || !Clans.IsLoaded) return;
            
            var clan = Clans?.Call<JObject>("GetClan", id);
            if (clan == null) return;
            
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"{clan["tag"]} Clan Details", FontSize = 18, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.95", AnchorMax = "1 .99"}
            }, panelName);
            
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Clan Members", FontSize = 14, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.91", AnchorMax = "1 0.95"}
            }, panelName);
            
            var bottom = 0.88f;
            var left = 0.1f;
            
            foreach (var member in clan["members"])
            {
                elements.Add(new CuiButton
                {
                    Button = {Command = $"EasyAdminSelected player {member}", Color = _config.ButtonColor},
                    RectTransform = {AnchorMin = $"{left} {bottom}", AnchorMax = $"{left + .2} {bottom + .025}"},
                    Text =
                    {
                        Text =
                        $"{BasePlayer.allPlayerList.FirstOrDefault(x => x.UserIDString == member.ToString())?.displayName} {(member.ToString() == clan["owner"].ToString() ? "(Owner)" : "")}",
                        FontSize = 11, Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    }
                }, panelName);
                
                left += 0.3f;
                if (!(left >= 0.75)) continue;
                left = 0.1f;
                bottom -= 0.03f;
            }
            
            if (CheckPermission(player, PluginPermissions.EasyAdminKick))
            bottom = AddControlPanel(bottom, elements, panelName, "Kick", id, "Clan");
            if (CheckPermission(player, PluginPermissions.EasyAdminBan))
            bottom = AddControlPanel(bottom, elements, panelName, "Ban", id, "Clan");
            if (CheckPermission(player, PluginPermissions.EasyAdminChat))
            bottom = AddControlPanel(bottom, elements, panelName, "Chat", id, "Clan");
            if (CheckPermission(player, PluginPermissions.EasyAdminVoice))
            AddControlPanel(bottom, elements, panelName, "Voice", id, "Clan");
        }
        
        void CreateEasyAdminMainContainer(BasePlayer player)
        {
            _openContainers.Add(player.userID);
            var elements = new CuiElementContainer();
            
            var panelName = elements.Add(new CuiPanel
            {
                Image = {Color = $"{_config.BackGroundColor}"},
                RectTransform = {AnchorMin = $"{_config.PanelAnchorMin}", AnchorMax = $"{_config.PanelAnchorMax}"},
                CursorEnabled = true, FadeOut = 0.1f
            }, "Overlay", "EasyAdminMainContainer");
            
            elements.Add(new CuiButton
            {
                Button = {Color = $"{_config.BorderColor}"},
                RectTransform = {AnchorMin = "0 0.945", AnchorMax = "1 1"},
                Text = {Text = string.Empty, Color = "0 0 0 0"}
            }, panelName);
            
            elements.Add(new CuiButton
            {
                Button = {Color = $"{_config.BorderColor}"},
                RectTransform = {AnchorMin = "0 0", AnchorMax = "0.001 1"},
                Text = {Text = string.Empty, Color = "0 0 0 0"}
            }, panelName);
            
            elements.Add(new CuiButton
            {
                Button = {Color = $"{_config.BorderColor}"},
                RectTransform = {AnchorMin = ".999 0", AnchorMax = "1 1"},
                Text = {Text = string.Empty, Color = "0 0 0 0"}
            }, panelName);
            
            elements.Add(new CuiButton
            {
                Button = {Color = $"{_config.BorderColor}"},
                RectTransform = {AnchorMin = "0 0", AnchorMax = "1 0.001"},
                Text = {Text = string.Empty, Color = "0 0 0 0"}
            }, panelName);
            
            
            elements.Add(new CuiButton
            {
                Button = {Command = "CloseEasyAdmin", Color = "1 1 1 0"},
                RectTransform = {AnchorMin = "0.948 0.948", AnchorMax = "1 0.995"},
                Text = {Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = _config.TextColor}
            }, panelName);
            
            
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"Easy Admin {_version}", Color = _config.TextColor, FontSize = 16,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = {AnchorMin = "0.3 0.95", AnchorMax = "0.7 0.998"}
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = {Command = "EasyAdminOpenPage clans 1", Color = _config.ButtonColor},
                RectTransform =
                {AnchorMin = $"{_config.ButtonClansAnchorMin}", AnchorMax = $"{_config.ButtonClansAnchorMax}"},
                Text = {Text = "Clans", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor}
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = {Command = "EasyAdminOpenPage logs 1", Color = _config.ButtonColor4},
                RectTransform =
                {AnchorMin = $"{_config.ButtonLogsAnchorMin}", AnchorMax = $"{_config.ButtonLogsAnchorMax}"},
                Text = {Text = "Logs", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor}
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = {Command = "EasyAdminOpenPage players 1", Color = _config.ButtonColor},
                RectTransform =
                {
                    AnchorMin = $"{_config.ButtonPlayersAnchorMin}", AnchorMax = $"{_config.ButtonPlayersAnchorMax}"
                },
                Text = {Text = "Players", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor}
            }, panelName);
            
            elements.Add(
            new CuiLabel
            {
                RectTransform = {AnchorMin = "0.8 0", AnchorMax = ".99 0.03"},
                Text = {Text = "By: Shady14u", FontSize = 8, Align = TextAnchor.MiddleRight, Color = "1 1 1 .4"}
            }, panelName);
            
            CuiHelper.AddUi(player, elements);
        }
        
        private void DestroyContainers(BasePlayer player, bool all)
        {
            CuiHelper.DestroyUi(player, "EasyAdminMainUi");
            CuiHelper.DestroyUi(player, "EasyAdminDetailContainer");
            CuiHelper.DestroyUi(player, "EasyAdminReasonsUi");
            
            if (!all) return;
            
            CuiHelper.DestroyUi(player, "EasyAdminMainContainer");
            _openContainers.Remove(player.userID);
        }
        
        void EasyAdminReasonsUi(BasePlayer player, string action, string actionDuration, string actionOwner)
        {
            DestroyContainers(player, false);
            var elements = new CuiElementContainer();
            var panelName =
            elements.Add(
            new CuiPanel
            {
                Image = {Color = "0 0 0 0"}, RectTransform = {AnchorMin = "0.0 0.1", AnchorMax = "1 0.95"},
                CursorEnabled = true
            }, "EasyAdminMainContainer", "EasyAdminReasonsUi");
            
            var bottom = 0.87f;
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = $"{action} Reason", FontSize = 18, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.89", AnchorMax = "1 0.97"}
            }, panelName);
            
            foreach (var reason in _config.Reasons.OrderBy(x => x.Value))
            {
                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"EasyAdminReasonSelected {action} {actionDuration} {actionOwner} {reason.Key}",
                        Color = _config.ButtonColor
                    },
                    RectTransform = {AnchorMin = $"0.1 {bottom}", AnchorMax = $"0.9 {bottom + 0.03f}"},
                    Text =
                    {
                        Text = $"{reason.Value}",
                        FontSize = 11, Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    }
                }, panelName);
                
                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Command = $"EasyAdminReasonSelected {action} {actionDuration} {actionOwner} {reason.Key} test",
                        Color = _config.ButtonColor2
                    },
                    RectTransform = {AnchorMin = $"0.905 {bottom}", AnchorMax = $"0.94 {bottom + 0.03f}"},
                    Text =
                    {
                        Text = "∞",
                        FontSize = 11, Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    }
                }, panelName);
                
                bottom -= 0.04f;
            }
            
            CuiHelper.AddUi(player, elements);
        }
        
        private SlotPosition GetSlotPosition(int itemPosition)
        {
            var col = itemPosition - 24;
            var row = 4;
            
            if (itemPosition < 6)
            {
                row = 0;
                col = itemPosition;
            }
            
            if (itemPosition >= 6 && itemPosition < 12)
            {
                row = 1;
                col = itemPosition - 6;
            }
            
            if (itemPosition >= 12 && itemPosition < 18)
            {
                row = 2;
                col = itemPosition - 12;
            }
            
            if (itemPosition >= 18 && itemPosition < 24)
            {
                row = 3;
                col = itemPosition - 18;
            }
            
            return itemPosition >= 40
            ? new SlotPosition {Row = 5, Col = itemPosition - 40, Location = "Wearable"}
            : new SlotPosition {Col = col, Row = row, Location = (row < 4) ? "Inventory" : "Belt"};
        }
        
        void OpenEasyAdminDetailContainer(BasePlayer player, string action, string id, int page)
        {
            DestroyContainers(player, false);
            var elements = new CuiElementContainer();
            var panelName = elements.Add(new CuiPanel
            {
                Image = {Color = $"0 0 0 0"}, RectTransform =
                {
                    AnchorMin = "0.0 0.1",
                    AnchorMax = $"1 0.95"
                },
                CursorEnabled = true
            }, "EasyAdminMainContainer", "EasyAdminDetailContainer");
            
            switch (action)
            {
                case "clan":
                ClanDetailsUi(player, id, elements, panelName);
                break;
                case "player":
                PlayerDetailsUi(player, id, elements, panelName);
                break;
                case "playerInventory":
                PlayerInventoryUi(player, id, elements, panelName);
                break;
                case "report":
                PlayerReportUi(player, id, elements, panelName, page);
                break;
            }
            
            CuiHelper.AddUi(player, elements);
        }
        
        void OpenEasyAdminMainPage(BasePlayer player, string action, int page)
        {
            if (string.IsNullOrEmpty(action)) action = "players";
            DestroyContainers(player, false);
            var elements = new CuiElementContainer();
            var panelName = elements.Add(new CuiPanel
            {
                Image = {Color = "0 0 0 0"}, RectTransform = {AnchorMin = "0 0.1", AnchorMax = "1 0.95"},
                CursorEnabled = true
            }, "EasyAdminMainContainer", "EasyAdminMainUi");
            
            var title = (action == "clans") ? GetMsg(PluginMessages.ClansTitle) :
            (action == "logs") ? GetMsg(PluginMessages.LogsTitle) : GetMsg(PluginMessages.PlayersTitle);
            
            elements.Add(new CuiLabel
            {
                Text = {Text = title, FontSize = 16, Align = TextAnchor.MiddleCenter, Color = _config.TextColor},
                RectTransform = {AnchorMin = "0 0.93", AnchorMax = "1 .99"}
            }, panelName);
            
            var top = 0.93f;
            var bottom = 0.9f;
            var left = 0.1f;
            var total = 0;
            switch (action)
            {
                case "clans":
                {
                    if (Clans && Clans.IsLoaded)
                    {
                        var clans = Clans?.Call<JArray>("GetAllClans");
                        total = clans.Count;
                        
                        foreach (var clan in clans.ToObject<List<string>>().OrderBy(x => x)
                        .Skip((page - 1) * 60).Take(60))
                        {
                            elements.Add(new CuiButton
                            {
                                Button = {Command = $"EasyAdminSelected clan {clan}", Color = _config.ButtonColor},
                                RectTransform = {AnchorMin = $"{left} {bottom}", AnchorMax = $"{left + 0.2} {top}"},
                                Text =
                                {
                                    Text = $"{clan}", FontSize = 11, Align = TextAnchor.MiddleCenter,
                                    Color = _config.TextColor
                                }
                            }, panelName);
                            
                            if (_storedData.PlayerBans != null && _storedData.PlayerBans.Any(x => x.BanTarget == clan))
                            {
                                elements.Add(new CuiButton
                                {
                                    Button =
                                    {
                                        Command = $"EasyAdminSelected report {clan}", Color = _config.ButtonColor2
                                    },
                                    RectTransform =
                                    {AnchorMin = $"{left + 0.204f} {bottom}", AnchorMax = $"{left + 0.23f} {top}"},
                                    Text =
                                    {
                                        Text = $"{_storedData.PlayerBans.Count(x => x.BanTarget == clan)}", FontSize = 11, Align = TextAnchor.MiddleCenter,
                                        Color = _config.TextColor
                                    }
                                }, panelName);
                            }
                            
                            left += 0.3f;
                            if (!(left >= 0.75)) continue;
                            left = 0.1f;
                            top -= 0.04f;
                            bottom -= 0.04f;
                        }
                    }
                    
                    break;
                }
                case "logs":
                var logs = _storedData.PlayerBans.OrderBy(x => x.BanTargetName)
                .Select(x => new {x.BanTarget, x.BanTargetName}).Distinct().ToList();
                total = logs.Count;
                foreach (var targetPlayer in logs.Skip((page - 1) * 60).Take(60))
                {
                    elements.Add(new CuiButton
                    {
                        Button =
                        {
                            Command = $"EasyAdminSelected report {targetPlayer.BanTarget}",
                            Color = _config.ButtonColor
                        },
                        RectTransform = {AnchorMin = $"{left} {bottom}", AnchorMax = $"{left + 0.2} {top}"},
                        Text =
                        {
                            Text =
                            $"{(string.IsNullOrEmpty(targetPlayer.BanTargetName) ? targetPlayer.BanTarget : targetPlayer.BanTargetName)}",
                            FontSize = 11, Align = TextAnchor.MiddleCenter,
                            Color = _config.TextColor
                        }
                    }, panelName);
                    
                    left += 0.3f;
                    if (!(left >= 0.75)) continue;
                    
                    left = 0.1f;
                    top -= 0.04f;
                    bottom -= 0.04f;
                }
                
                break;
                
                case "players":
                var playerList = BasePlayer.allPlayerList.OrderBy(x => x.displayName).ToList();
                total = playerList.Count;
                
                foreach (var targetPlayer in playerList.Skip((page - 1) * 60).Take(60))
                {
                    var isActive =
                    BasePlayer.activePlayerList.Any(x => x.UserIDString == targetPlayer.UserIDString);
                    elements.Add(new CuiButton
                    {
                        Button =
                        {
                            Command = $"EasyAdminSelected player {targetPlayer.userID}",
                            Color = isActive ? _config.ButtonColor : _config.ButtonSleeper
                        },
                        RectTransform = {AnchorMin = $"{left} {bottom}", AnchorMax = $"{left + 0.2} {top}"},
                        Text =
                        {
                            Text = $"{targetPlayer.displayName}", FontSize = 11, Align = TextAnchor.MiddleCenter,
                            Color = _config.TextColor
                        }
                    }, panelName);
                    
                    
                    if (_storedData.PlayerBans != null &&
                    _storedData.PlayerBans.Any(x => x.BanTarget == targetPlayer.UserIDString))
                    {
                        elements.Add(new CuiButton
                        {
                            Button =
                            {
                                Command = $"EasyAdminSelected report {targetPlayer.userID}",
                                Color = _config.ButtonColor2
                            },
                            RectTransform =
                            {AnchorMin = $"{left + 0.204f} {bottom}", AnchorMax = $"{left + 0.23f} {top}"},
                            Text =
                            {
                                Text = $"{_storedData.PlayerBans.Count(x => x.BanTarget == targetPlayer.UserIDString)}", FontSize = 11, Align = TextAnchor.MiddleCenter,
                                Color = _config.TextColor
                            }
                        }, panelName);
                    }
                    
                    left += 0.3f;
                    if (!(left >= 0.75)) continue;
                    
                    left = 0.1f;
                    top -= 0.04f;
                    bottom -= 0.04f;
                }
                
                break;
            }
            
            
            if (total > page * 60)
            {
                elements.Add(
                new CuiButton
                {
                    Button = {Command = $"EasyAdminOpenPage {action} {page + 1}", Color = _config.ButtonColor},
                    RectTransform =
                    {AnchorMin = $"{_config.PageNextAnchorMin}", AnchorMax = $"{_config.PageNextAnchorMax}"},
                    Text =
                    {
                        Text = GetMsg(PluginMessages.RightArrow), FontSize = 11,
                        Align = TextAnchor.MiddleCenter, Color = _config.TextColor
                    },
                }, panelName);
            }
            
            elements.Add(
            new CuiLabel
            {
                RectTransform = {AnchorMin = $"0.3835 0", AnchorMax = $"0.6165 0.04"},
                Text =
                {
                    Text = $"Page {page} of {(total / 60) + 1}", FontSize = 14,
                    Align = TextAnchor.MiddleCenter, Color = _config.TextColor
                },
            }, panelName);
            
            
            if (page > 1)
            {
                elements.Add(
                new CuiButton
                {
                    Button =
                    {
                        Command = $"EasyAdminOpenPage {action} {page - 1}", Color = _config.ButtonColor
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{_config.PageBackAnchorMin}",
                        AnchorMax = $"{_config.PageBackAnchorMax}"
                    },
                    Text =
                    {
                        Text = GetMsg(PluginMessages.LeftArrow), FontSize = 13,
                        Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    },
                }, panelName);
            }
            
            
            CuiHelper.AddUi(player, elements);
        }
        
        private void PlayerDetailsUi(BasePlayer player, string id, CuiElementContainer elements, string panelName)
        {
            var targetPlayer = BasePlayer.allPlayerList.FirstOrDefault(x => x.UserIDString == id);
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = $"{targetPlayer?.displayName} Details", FontSize = 18, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.93", AnchorMax = "1 .99"}
            }, panelName);
            
            if (CheckPermission(player, PluginPermissions.EasyAdminViewInventory))
            {
                elements.Add(
                new CuiButton
                {
                    Button = {Command = $"EasyAdminSelected playerInventory {id}", Color = _config.ButtonColor},
                    Text =
                    {
                        Text = "View Inventory", FontSize = 12, Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    },
                    RectTransform = {AnchorMin = "0.767 0.945", AnchorMax = ".967 0.975"}
                }, panelName);
            }
            
            var bottom = 0.94f;
            if (CheckPermission(player, PluginPermissions.EasyAdminKick))
            bottom = AddControlPanel(bottom, elements, panelName, "Kick", id, "Player");
            if (CheckPermission(player, PluginPermissions.EasyAdminBan))
            bottom = AddControlPanel(bottom, elements, panelName, "Ban", id, "Player");
            if (CheckPermission(player, PluginPermissions.EasyAdminChat))
            bottom = AddControlPanel(bottom, elements, panelName, "Chat", id, "Player");
            if (CheckPermission(player, PluginPermissions.EasyAdminVoice))
            AddControlPanel(bottom, elements, panelName, "Voice", id, "Player");
            if(CheckPermission(player,PluginPermissions.EasyAdminTeleport))
            AddTPMenu(elements, panelName, id);
            
            
        }
        
        private void AddTPMenu(CuiElementContainer elements, string panelName, string id)
        {
            elements.Add(
            new CuiButton
            {
                Button = { Command = $"PlayerAction TP2P {id}", Color = _config.ButtonColor },
                RectTransform = {AnchorMin = $"0.2835 0.0", AnchorMax = $"0.4 0.04"},
                Text = { Text = GetMsg(PluginMessages.TP2Player), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor }
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = { Command = $"PlayerAction TPP2ME {id}", Color = _config.ButtonColor },
                RectTransform = { AnchorMin = $"0.41 0.0", AnchorMax = $"0.53 0.04" },
                Text = { Text = GetMsg(PluginMessages.TPPlayer2ME), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor }
            }, panelName);
        }
        
        private void PlayerInventoryUi(BasePlayer player, string id, CuiElementContainer elements, string panelName)
        {
            var targetPlayer = GetPlayer(id);
            if (targetPlayer == null) return;
            var inventory = targetPlayer.inventory.containerMain;
            var idx = 0;
            
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = $"{targetPlayer.displayName} Inventory Details", FontSize = 18,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.93", AnchorMax = "1 .99"}
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = {Command = $"EasyAdminSelected player {id}", Color = _config.ButtonColor},
                RectTransform = {AnchorMin = "0.65 0.85", AnchorMax = "0.8 0.88"},
                Text = {Text = "Back", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor}
            }, panelName);
            
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = "Inventory", FontSize = 16, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = ".4 0.84", AnchorMax = ".6 0.89"}
            }, panelName);
            
            foreach (var item in inventory.itemList.OrderBy(x => x.position))
            {
                while (idx < item.position)
                {
                    //create empty box here
                    AddInventoryPlaceHolder(elements, panelName, idx);
                    idx++;
                }
                
                //create Image here
                AddInventoryItem(elements, panelName, id, item.info, item.skin, item.amount, item.position, player);
                idx = item.position + 1;
            }
            
            while (idx <= 23)
            {
                //create empty box here
                AddInventoryPlaceHolder(elements, panelName, idx);
                idx++;
            }
            
            //Get the belt items here
            for (int i = 0; i < 6; i++)
            {
                var item = targetPlayer.Belt.GetItemInSlot(i);
                if (item != null)
                {
                    AddInventoryItem(elements, panelName, id, item.info, item.skin, item.amount, 24 + i, player);
                }
                else
                {
                    AddInventoryPlaceHolder(elements, panelName, 24 + i);
                }
            }
            
            idx = 0;
            //Get the wearable items here
            foreach (var item in targetPlayer.inventory.containerWear.itemList.OrderBy(x => x.position))
            {
                while (idx < item.position)
                {
                    //create empty box here
                    AddInventoryPlaceHolder(elements, panelName, 40 + idx);
                    idx++;
                }
                
                //create Image here
                AddInventoryItem(elements, panelName, id, item.info, item.skin, item.amount, 40 + item.position, player);
                idx = item.position + 1;
            }
            
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = "Belt", FontSize = 16, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.36", AnchorMax = "1 0.41"}
            }, panelName);
            
            elements.Add(
            new CuiLabel
            {
                Text =
                {
                    Text = "Wearables", FontSize = 16, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.188", AnchorMax = "1 0.238"}
            }, panelName);
            
            elements.Add(
            new CuiButton
            {
                Button = { Command = $"RemoveAllInventoryItems {id}", Color = _config.ButtonColor2 },
                RectTransform = { AnchorMin = "0.45 0.02", AnchorMax = "0.55 0.06" },
                Text = { Text = "Clear All", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = _config.TextColor }
            }, panelName);
        }
        
        private void PlayerReportUi(BasePlayer player, string id, CuiElementContainer elements, string panelName,
        int page)
        {
            var targetPlayer = BasePlayer.allPlayerList.FirstOrDefault(x => x.UserIDString == id);
            var targetName = id;
            if (targetPlayer != null)
            {
                targetName = targetPlayer.displayName;
            }
            
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = $"{targetName} Ban Details", FontSize = 18, Align = TextAnchor.MiddleCenter,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = "0 0.93", AnchorMax = "1 .99"}
            }, panelName);
            
            elements.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Remove Ban / Mute by clicking on the", FontSize = 10, Align = TextAnchor.MiddleRight,
                    Color = _config.TextColor
                },
                RectTransform = {AnchorMin = ".2 0.87", AnchorMax = ".6 0.91"}
            }, panelName);
            
            elements.Add(new CuiButton
            {
                Button = {Color = _config.ButtonColor2},
                Text = {Text = "X", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = _config.TextColor},
                RectTransform = {AnchorMin = "0.612 0.880", AnchorMax = $".63 .9"}
            }, panelName);
            
            elements.Add(new CuiButton
            {
                Button = {Color = _config.ButtonColor2},
                Text = {Text = string.Empty, Color = _config.TextColor},
                RectTransform = {AnchorMin = "0.025 0.872", AnchorMax = $"0.975 0.873"}
            }, panelName);
            
            var bottom = 0.83f;
            
            var reports = _storedData.PlayerBans.Where(x => x.BanTarget == id).OrderByDescending(x => x.BanDate)
            .ToList();
            var total = reports.Count;
            
            foreach (var playerBan in reports.Skip(20 * (page - 1)).Take(20))
            {
                var adminUser = GetPlayer(playerBan.AdminUser);
                var reportText = GetMsg(PluginMessages.ReportText).Replace("{banDate}",
                playerBan.BanDate.ToString(_config.ReportDateFormat))
                .Replace("{banText}", playerBan.BanText).Replace("{reason}", playerBan.BanReason)
                .Replace("{bannedBy}", adminUser?.displayName);
                
                if (CheckPermission(player, PluginPermissions.EasyAdminDeleteReports))
                {
                    elements.Add(
                    new CuiButton
                    {
                        Button =
                        {
                            Command = $"DeleteReportItem {playerBan.BanDate:ddMMyyyyhhmmss} {id} {page}",
                            Color = _config.ButtonColor2
                        },
                        Text =
                        {
                            Text = "X", FontSize = 11, Align = TextAnchor.MiddleCenter,
                            Color = _config.TextColor
                        },
                        RectTransform =
                        {AnchorMin = $"0.065 {bottom + 0.0025}", AnchorMax = $".085 {bottom + 0.0275}"}
                    }, panelName);
                }
                
                elements.Add(
                new CuiLabel
                {
                    Text =
                    {
                        Text = reportText, FontSize = 11, Align = TextAnchor.MiddleLeft,
                        Color = _config.TextColor
                    },
                    RectTransform = {AnchorMin = $"0.1 {bottom}", AnchorMax = $"1 {bottom + 0.03f}"}
                }, panelName);
                
                bottom -= 0.035f;
            }
            
            if (total > page * 20)
            {
                elements.Add(
                new CuiButton
                {
                    Button = {Command = $"EasyAdminSelected report {id} {page + 1}", Color = _config.ButtonColor},
                    RectTransform =
                    {AnchorMin = $"{_config.PageNextAnchorMin}", AnchorMax = $"{_config.PageNextAnchorMax}"},
                    Text =
                    {
                        Text = GetMsg(PluginMessages.RightArrow), FontSize = 11,
                        Align = TextAnchor.MiddleCenter, Color = _config.TextColor
                    },
                }, panelName);
            }
            
            elements.Add(
            new CuiLabel
            {
                RectTransform = {AnchorMin = $"0.3835 0", AnchorMax = $"0.6165 0.04"},
                Text =
                {
                    Text = $"Page {page} of {(total / 20) + 1}", FontSize = 14,
                    Align = TextAnchor.MiddleCenter, Color = _config.TextColor
                },
            }, panelName);
            
            
            if (page > 1)
            {
                elements.Add(
                new CuiButton
                {
                    Button =
                    {
                        Command = $"EasyAdminSelected report {id} {page - 1}", Color = _config.ButtonColor
                    },
                    RectTransform =
                    {
                        AnchorMin = $"{_config.PageBackAnchorMin}",
                        AnchorMax = $"{_config.PageBackAnchorMax}"
                    },
                    Text =
                    {
                        Text = GetMsg(PluginMessages.LeftArrow), FontSize = 13,
                        Align = TextAnchor.MiddleCenter,
                        Color = _config.TextColor
                    },
                }, panelName);
            }
        }
        
        #endregion
        #endregion

    }

}
