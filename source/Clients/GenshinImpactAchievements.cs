using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommonPluginsShared;
using CommonPluginsShared.Extensions;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SuccessStory.Models;
using SuccessStory.Views;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    class GenshinImpactAchivementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<AchievementSource, GenericAchievements> Providers, Dictionary<AchievementSource, ISearchableManualAchievements> ManualSearchProviders, Dictionary<AchievementSource, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            Providers[AchievementSource.GenshinImpact] = new GenshinImpactAchievements();
        }
    }
    class GenshinImpactAchievements : GenericAchievements
    {
        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (game.Name.IsEqual("Genshin Impact") && !ignoreSpecial)
            {
                return 200;
            }
            return 0;
        }
        public override dynamic GetOneGameView(SuccessStorySettingsViewModel pluginSettings, Game gameMenu)
        {
            if (pluginSettings.Settings.EnableGenshinImpact && gameMenu.Name.IsEqual("Genshin Impact"))
            {
                return new SuccessStoryCategoryView(gameMenu);
            }
            return null;
        }
        public override bool HasManualMenuOverride(IPlayniteAPI playniteApi, SuccessStorySettingsViewModel pluginSettings, Game gameMenu)
        {
            return gameMenu.Name.IsEqual("Genshin Impact");
        }
        public override bool BuildManualMenuOverride(IPlayniteAPI playniteApi, SuccessStorySettingsViewModel pluginSettings, GameAchievements gameAchievements, Game gameMenu, List<GameMenuItem> gameMenuItems)
        {
            if (gameMenu.Name.IsEqual("Genshin Impact"))
            {
                if (pluginSettings.Settings.EnableGenshinImpact)
                {
                    if (!gameAchievements.HasData)
                    {
                        gameMenuItems.Add(new GameMenuItem
                        {
                            MenuSection = resources.GetString("LOCSuccessStory"),
                            Description = resources.GetString("LOCAddGenshinImpact"),
                            Action = (mainMenuItem) =>
                            {
                                PluginDatabase.Remove(gameMenu);
                                GetGenshinImpact(gameMenu);
                            }
                        });
                    }
                    else
                    {
                        gameMenuItems.Add(new GameMenuItem
                        {
                            MenuSection = resources.GetString("LOCSuccessStory"),
                            Description = resources.GetString("LOCEditGame"),
                            Action = (mainMenuItem) =>
                            {
                                var ViewExtension = new SuccessStoryEditManual(gameMenu);
                                Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(playniteApi, resources.GetString("LOCSuccessStory"), ViewExtension);
                                windowExtension.ShowDialog();
                            }
                        });

                        gameMenuItems.Add(new GameMenuItem
                        {
                            MenuSection = resources.GetString("LOCSuccessStory"),
                            Description = resources.GetString("LOCRemoveTitle"),
                            Action = (gameMenuItem) =>
                            {
                                var TaskIntegrationUI = Task.Run(() =>
                                {
                                    PluginDatabase.Remove(gameMenu);
                                });
                            }
                        });
                    }
                }
                return true;
            }
            return false;
        }
        public override GameAchievements RefreshManualOverrideData(Game game)
        {
            logger.Info($"RefreshGenshinImpact({game?.Name} - {game?.Id})");
            GameAchievements gameAchievements = null;

            try
            {
                GenshinImpactAchievements genshinImpactAchievements = new GenshinImpactAchievements();
                gameAchievements = genshinImpactAchievements.GetAchievements(game);
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return gameAchievements;
        }


        public void GetGenshinImpact(Game game)
        {
            try
            {
                GenshinImpactAchievements genshinImpactAchievements = new GenshinImpactAchievements();
                GameAchievements gameAchievements = genshinImpactAchievements.GetAchievements(game);
                PluginDatabase.AddOrUpdate(gameAchievements);
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }








        private static string UrlTextMap                => @"https://raw.githubusercontent.com/theBowja/GenshinData-1/master/TextMap/TextMap{0}.json";
        private static string UrlAchievementsCategory   => @"https://raw.githubusercontent.com/theBowja/GenshinData-1/master/ExcelBinOutput/AchievementGoalExcelConfigData.json";
        private static string UrlAchievements           => @"https://raw.githubusercontent.com/theBowja/GenshinData-1/master/ExcelBinOutput/AchievementExcelConfigData.json";

        public GenshinImpactAchievements() : base("GenshinImpact", CodeLang.GetGenshinLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();

            try
            {
                string Url = string.Format(UrlTextMap, LocalLang.ToUpper());
                string TextMapString = Web.DownloadStringData(Url).GetAwaiter().GetResult();
                Serialization.TryFromJson(TextMapString, out dynamic TextMap);
                if (TextMap == null)
                {
                    throw new Exception($"No data from {Url}");
                }

                string AchievementsString = Web.DownloadStringData(UrlAchievements).GetAwaiter().GetResult();
                Serialization.TryFromJson(AchievementsString, out List<GenshinImpactAchievementData> GenshinImpactAchievements);
                if (GenshinImpactAchievements == null)
                {
                    throw new Exception($"No data from {UrlAchievements}");
                }

                string AchievementsCategoryString = Web.DownloadStringData(UrlAchievementsCategory).GetAwaiter().GetResult();
                Serialization.TryFromJson(AchievementsCategoryString, out List<GenshinImpactAchievementsCategory> GenshinImpactAchievementsCategory);
                if (GenshinImpactAchievementsCategory == null)
                {
                    throw new Exception($"No data from {UrlAchievementsCategory}");
                }

                GenshinImpactAchievements.ForEach(x => 
                {
                    GenshinImpactAchievementsCategory giCategory = GenshinImpactAchievementsCategory.Find(y => y.Id != null && (int)y.Id == x.GoalId);
                    int CategoryOrder = giCategory?.OrderId ?? 0;
                    string Category = TextMap[giCategory?.NameTextMapHash?.ToString()]?.Value;
                    string CategoryIcon = string.Format("GenshinImpact\\ac_{0}.png", CategoryOrder);

                    AllAchievements.Add(new Achievements
                    {
                        ApiName = x.Id.ToString(),
                        Name = TextMap[x.TitleTextMapHash?.ToString()]?.Value,
                        Description = TextMap[x.DescTextMapHash?.ToString()]?.Value,
                        UrlUnlocked = "GenshinImpact\\ac.png",

                        CategoryOrder = CategoryOrder,
                        CategoryIcon = CategoryIcon,
                        Category = Category,

                        DateUnlocked = default(DateTime)
                    });

                    gameAchievements.IsManual = true;
                    gameAchievements.SourcesLink = new CommonPluginsShared.Models.SourceLink
                    {
                        GameName = "Genshin Impact",
                        Name = "GitHub",
                        Url = "https://github.com/theBowja/GenshinData-1"
                    };
                });
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            if (AllAchievements.Count > 0)
            {
                AllAchievements = AllAchievements.Where(x => !x.Name.IsNullOrEmpty()).ToList();
            }

            gameAchievements.Items = AllAchievements;
            gameAchievements.SetRaretyIndicator();

            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            return true;
        }

        public override bool EnabledInSettings()
        {
            return PluginDatabase.PluginSettings.Settings.EnableGenshinImpact;
        }
        #endregion
    }
}
