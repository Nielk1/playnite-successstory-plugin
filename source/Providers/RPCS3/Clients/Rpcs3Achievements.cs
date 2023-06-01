﻿using Playnite.SDK.Models;
using CommonPluginsShared;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommonPluginsShared.Extensions;
using Playnite.SDK;
using static SuccessStory.Services.SuccessStoryDatabase;
using static CommonPluginsShared.PlayniteTools;
using System.Collections.ObjectModel;

namespace SuccessStory.Clients
{
    class Rpcs3AchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<string, GenericAchievements> Providers, Dictionary<string, ISearchableManualAchievements> ManualSearchProviders, Dictionary<string, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            Providers[AchievementSource.RPCS3] = new Rpcs3Achievements();
        }
    }
    class Rpcs3Achievements : GenericAchievements
    {
        internal static new ILogger logger => LogManager.GetLogger();

        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game)
        {
            if (game.GameActions != null)
            {
                foreach (GameAction action in game.GameActions)
                {
                    if (!action.IsPlayAction || action.EmulatorId == Guid.Empty)
                    {
                        continue;
                    }

                    Emulator emulator = API.Instance.Database.Emulators.FirstOrDefault(e => e.Id == action.EmulatorId);
                    if (emulator == null)
                    {
                        continue;
                    }

                    foreach (var Provider in AchievementProviders)
                    {
                        if (PlayniteTools.GameUseRpcs3(game) && settings.EnableRpcs3Achievements)
                        {
                            return 50;
                        }
                    }
                }
            }
            return 0;
        }



        public override void GetFilterItems(bool isRetroAchievements, Collection<ListSource> filterSourceItems)
        {
            bool retroAchievementsEnabled = PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView && PluginDatabase.PluginSettings.Settings.EnableRetroAchievements;

            if ((retroAchievementsEnabled && !isRetroAchievements) || !retroAchievementsEnabled)
            {
                if (PluginDatabase.PluginSettings.Settings.EnableRpcs3Achievements)
                {
                    string icon = TransformIcon.Get("RPCS3") + " ";
                    filterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "RPCS3", SourceNameShort = "Rpcs3", IsCheck = false });
                }
            }
        }


        public Rpcs3Achievements() : base("RPCS3")
        {
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();


            if (IsConfigured())
            {
                List<string> TrophyDirectories = FindTrophyGameFolder(game);
                string TrophyFile = "TROPUSR.DAT";
                string TrophyFileDetails = "TROPCONF.SFM";


                // Directory control
                if (TrophyDirectories.Count == 0)
                {
                    logger.Warn($"No Trophy directoy found for {game.Name}");
                    return gameAchievements;
                }

                foreach (string TrophyDirectory in TrophyDirectories)
                {
                    AllAchievements = new List<Achievements>();

                    if (!File.Exists(Path.Combine(TrophyDirectory, TrophyFile)))
                    {
                        logger.Warn($"File {TrophyFile} not found for {game.Name} in {Path.Combine(TrophyDirectory, TrophyFile)}");
                        continue;
                    }
                    if (!File.Exists(Path.Combine(TrophyDirectory, TrophyFileDetails)))
                    {
                        logger.Warn($"File {TrophyFileDetails} not found for {game.Name} in {Path.Combine(TrophyDirectory, TrophyFileDetails)}");
                        continue;
                    }


                    int TrophyCount = 0;
                    List<string> TrophyHexData = new List<string>();


                    // Trophies details
                    XDocument TrophyDetailsXml = XDocument.Load(Path.Combine(TrophyDirectory, TrophyFileDetails));

                    var GameName = TrophyDetailsXml.Descendants("title-name").FirstOrDefault().Value.Trim();

                    foreach (XElement TrophyXml in TrophyDetailsXml.Descendants("trophy"))
                    {
                        Console.WriteLine(TrophyXml);

                        int.TryParse(TrophyXml.Attribute("id").Value, out int TrophyDetailsId);
                        string TrophyType = TrophyXml.Attribute("ttype").Value;
                        string Name = TrophyXml.Element("name").Value;
                        string Description = TrophyXml.Element("detail").Value;

                        int Percent = 100;
                        if (TrophyType.IsEqual("s"))
                        {
                            Percent = 30;
                        }
                        if (TrophyType.IsEqual("g"))
                        {
                            Percent = 10;
                        }

                        AllAchievements.Add(new Achievements
                        {
                            ApiName = string.Empty,
                            Name = Name,
                            Description = Description,
                            UrlUnlocked = CopyTrophyFile(TrophyDirectory, "TROP" + TrophyDetailsId.ToString("000") + ".png"),
                            UrlLocked = string.Empty,
                            DateUnlocked = default(DateTime),
                            Percent = Percent,

                            CategoryRpcs3 = TrophyDirectories.Count > 1 ? GameName : null
                        });
                    }


                    TrophyCount = AllAchievements.Count;


                    // Trophies data
                    byte[] TrophyByte = File.ReadAllBytes(Path.Combine(TrophyDirectory, TrophyFile));
                    string hex = Tools.ToHex(TrophyByte);

                    List<string> splitHex = hex.Split(new[] { "0000000600000060000000" }, StringSplitOptions.None).ToList();
                    for (int i = (splitHex.Count - 1); i >= (splitHex.Count - TrophyCount); i--)
                    {
                        TrophyHexData.Add(splitHex[i]);
                    }
                    TrophyHexData.Reverse();

                    foreach (string HexData in TrophyHexData)
                    {
                        string stringHexId = HexData.Substring(0, 2);
                        int Id = (int)Int64.Parse(stringHexId, System.Globalization.NumberStyles.HexNumber);

                        string Unlocked = HexData.Substring(18, 8);
                        bool IsUnlocked = (Unlocked == "00000001");

                        // No unlock time
                        if (IsUnlocked)
                        {
                            AllAchievements[Id].DateUnlocked = new DateTime(1982, 12, 15, 0, 0, 0, 0);
                        }
                    }

                    AllAchievements.ForEach(x => gameAchievements.Items.Add(x));
                }

                gameAchievements.Handler = new MainAchievementHandler("RPCS3", null);
            }
            else
            {
                ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsRpcs3BadConfig"));
            }

            gameAchievements.SetRaretyIndicator();
            return gameAchievements;
        }

        public override string DecorateImagePath(string imagePath)
        {
            if (imagePath?.Contains("rpcs3", StringComparison.InvariantCultureIgnoreCase) ?? false)
                return Path.Combine(PluginDatabase.Paths.PluginUserDataPath, imagePath);
            return null;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            if (CachedConfigurationValidationResult == null)
            {
                CachedConfigurationValidationResult = IsConfigured();

                if (!(bool)CachedConfigurationValidationResult)
                {
                    ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsRpcs3BadConfig"));
                }
            }
            else if (!(bool)CachedConfigurationValidationResult)
            {
                ShowNotificationPluginErrorMessage();
            }

            return (bool)CachedConfigurationValidationResult;
        }


        public override bool IsConfigured()
        {
            if (PluginDatabase.PluginSettings.Settings.Rpcs3InstallationFolder.IsNullOrEmpty())
            {
                logger.Warn("No RPCS3 configured folder");
                return false;
            }

            if (!Directory.Exists(Path.Combine(PluginDatabase.PluginSettings.Settings.Rpcs3InstallationFolder, "trophy")))
            {
                logger.Warn($"No RPCS3 trophy folder in {PluginDatabase.PluginSettings.Settings.Rpcs3InstallationFolder}");
                return false;
            }
            
            return true;
        }

        public override bool EnabledInSettings()
        {
            return PluginDatabase.PluginSettings.Settings.EnableRpcs3Achievements;
        }
        #endregion


        #region RPCS3
        private List<string> FindTrophyGameFolder(Game game)
        {
            List<string> TrophyGameFolder = new List<string>();

            List<string> FoldersPath = new List<string> { PluginDatabase.PluginSettings.Settings.Rpcs3InstallationFolder };
            PluginDatabase.PluginSettings.Settings.Rpcs3InstallationFolders?.ForEach(x => FoldersPath.Add(x.FolderPath));

            FoldersPath.ForEach(x =>
            {
                string TrophyFolder = Path.Combine(x, "trophy");
                string GameTrophyFolder = Path.Combine(game.InstallDirectory, "..", "TROPDIR");
                GameTrophyFolder = API.Instance.ExpandGameVariables(game, GameTrophyFolder);

                logger.Info($"TrophyFolder: {TrophyFolder}");
                logger.Info($"GameTrophyFolder: {GameTrophyFolder}");

                try
                {
                    if (Directory.Exists(GameTrophyFolder))
                    {
                        Parallel.ForEach(Directory.EnumerateDirectories(GameTrophyFolder), (objectDirectory, state) =>
                        {
                            DirectoryInfo di = new DirectoryInfo(objectDirectory);
                            string NameFolder = di.Name;

                            if (Directory.Exists(Path.Combine(TrophyFolder, NameFolder)))
                            {
                                TrophyGameFolder.Add(Path.Combine(TrophyFolder, NameFolder));
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginDatabase.PluginName);
                }
            });

            return TrophyGameFolder;
        }


        private string CopyTrophyFile(string TrophyDirectory, string TrophyFile)
        {
            DirectoryInfo di = new DirectoryInfo(TrophyDirectory);
            string NameFolder = di.Name;

            if (!Directory.Exists(Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3")))
            {
                Directory.CreateDirectory(Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3"));
            }
            if (!Directory.Exists(Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3", NameFolder)))
            {
                Directory.CreateDirectory(Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3", NameFolder));
            }

            try
            {
                if (!File.Exists(Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3", NameFolder, TrophyFile)))
                {
                    File.Copy(Path.Combine(TrophyDirectory, TrophyFile), Path.Combine(PluginDatabase.Paths.PluginUserDataPath, "rpcs3", NameFolder, TrophyFile));
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return Path.Combine("rpcs3", NameFolder, TrophyFile);
        }
        #endregion
    }
}
