using LiveCharts;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using CommonPluginsShared;
using CommonPluginsShared.Collections;
using CommonPluginsControls.LiveChartsCommon;
using SuccessStory.Clients;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static SuccessStory.Clients.TrueAchievements;
using System.Windows.Threading;
using System.Windows;
using System.Threading;
using SuccessStory.Views;
using CommonPluginsShared.Converters;
using CommonPluginsControls.Controls;
using System.Diagnostics;
using System.Text.RegularExpressions;
using static CommonPluginsShared.PlayniteTools;
using CommonPluginsShared.Extensions;
using System.Reflection;

namespace SuccessStory.Services
{
    public class SuccessStoryDatabase : PluginDatabaseObject<SuccessStorySettingsViewModel, SuccessStoryCollection, GameAchievements, Achievements>
    {
        public SuccessStory Plugin;

        private bool _isRetroachievements { get; set; }

        private static Dictionary<string, GenericAchievements> _achievementProviders { get; set; }
        private static Dictionary<string, ISearchableManualAchievements> _achievementManualSearchProviders { get; set; }
        private static Dictionary<string, IMetadataAugmentAchievements> _achievementMetadataAugmenters { get; set; }
        private static object _achievementProvidersLock => new object();
        internal static Dictionary<string, GenericAchievements> AchievementProviders
        {
            get
            {
                PrepareAchievmentProviders();
                return _achievementProviders;
            }
        }
        internal static Dictionary<string, ISearchableManualAchievements> AchievementManualSearchProviders
        {
            get
            {
                PrepareAchievmentProviders();
                return _achievementManualSearchProviders;
            }
        }
        internal static Dictionary<string, IMetadataAugmentAchievements> AchievementMetadataAugmenters
        {
            get
            {
                PrepareAchievmentProviders();
                return _achievementMetadataAugmenters;
            }
        }
        private static void PrepareAchievmentProviders()
        {
            lock (_achievementProvidersLock)
            {
                if (_achievementProviders == null)
                {
                    _achievementProviders = new Dictionary<string, GenericAchievements>();
                    _achievementManualSearchProviders = new Dictionary<string, ISearchableManualAchievements>();
                    _achievementMetadataAugmenters = new Dictionary<string, IMetadataAugmentAchievements>();

                    // for now just scan ourself, we might be able to dynamicly load from other plugins but we'd need to remove all tight coupling first
                    foreach (Type item in typeof(IAchievementFactory).GetTypeInfo().Assembly.GetTypes())
                    {
                        if (item.GetInterfaces().Contains(typeof(IAchievementFactory)))
                        {
                            ConstructorInfo[] cons = item.GetConstructors();
                            foreach (ConstructorInfo con in cons)
                            {
                                try
                                {
                                    /*ParameterInfo[] @params = con.GetParameters();
                                    object[] paramList = new object[@params.Length];
                                    for (int i = 0; i < @params.Length; i++)
                                    {
                                        paramList[i] = ServiceProvider.GetService(@params[i].ParameterType);
                                    }

                                    IAchievementFactory plugin = (IAchievementFactory)Activator.CreateInstance(item, paramList);
                                    */
                                    IAchievementFactory plugin = (IAchievementFactory)Activator.CreateInstance(item);
                                    plugin.BuildClient(_achievementProviders, _achievementManualSearchProviders, _achievementMetadataAugmenters);
                                }
                                catch { }
                            }
                        }
                    }

                }
            }
        }

        public SuccessStoryDatabase(IPlayniteAPI PlayniteApi, SuccessStorySettingsViewModel PluginSettings, string PluginUserDataPath) : base(PlayniteApi, PluginSettings, "SuccessStory", PluginUserDataPath)
        {
            TagBefore = "[SS]";
        }


        public void InitializeClient(SuccessStory Plugin)
        {
            this.Plugin = Plugin;
        }


        protected override bool LoadDatabase()
        {
            try
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                Database = new SuccessStoryCollection(Paths.PluginDatabasePath);
                Database.SetGameInfo<Achievements>(PlayniteApi);

                DeleteDataWithDeletedGame();

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                logger.Info($"LoadDatabase with {Database.Count} items - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)}");
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
                return false;
            }

            return true;
        }


        public void GetManual(Game game)
        {
            try
            {
                GameAchievements gameAchievements = GetDefault(game);

                SuccessStoreGameSelection ViewExtension = new SuccessStoreGameSelection(game);
                Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, resources.GetString("LOCSuccessStory"), ViewExtension);
                windowExtension.ShowDialog();

                if (ViewExtension.gameAchievements != null)
                {
                    gameAchievements = ViewExtension.gameAchievements;
                    gameAchievements.IsManual = true;
                }

                gameAchievements = SetEstimateTimeToUnlock(game, gameAchievements);
                AddOrUpdate(gameAchievements);

                Common.LogDebug(true, $"GetManual({game.Id.ToString()}) - gameAchievements: {Serialization.ToJson(gameAchievements)}");
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }
        }

        // TODO: tight coupling of manual refresh
        public GameAchievements RefreshManual(Game game)
        {
            logger.Info($"RefreshManual({game?.Name} - {game?.Id})");
            GameAchievements gameAchievements = null;

            try
            {
                gameAchievements = Get(game, true);
                if (gameAchievements != null && gameAchievements.HasData)
                {
                    foreach (var achievementManualSearchProvider in AchievementManualSearchProviders)
                    {
                        if (achievementManualSearchProvider.Value.CanDoManualAchievements(game, gameAchievements))
                        {
                            gameAchievements = achievementManualSearchProvider.Value.DoManualAchievements(game, gameAchievements);
                            break;
                        }
                    }

                    Common.LogDebug(true, $"RefreshManual({game.Id.ToString()}) - gameAchievements: {Serialization.ToJson(gameAchievements)}");
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            return gameAchievements;
        }



        public override GameAchievements Get(Guid Id, bool OnlyCache = false, bool Force = false)
        {
            GameAchievements gameAchievements = base.GetOnlyCache(Id);
            Game game = PlayniteApi.Database.Games.Get(Id);

            // Get from web
            if ((gameAchievements == null && !OnlyCache) || Force)
            {
                gameAchievements = GetWeb(Id);
                AddOrUpdate(gameAchievements);
            }
            else if (gameAchievements == null)
            {
                if (game != null)
                {
                    gameAchievements = GetDefault(game);
                    Add(gameAchievements);
                }
            }

            return gameAchievements;
        }

        // TODO: remove tight coupling here
        /// <summary>
        /// Called by Refresh on non manual type sources
        /// </summary>
        /// <param name="game"></param>
        public override GameAchievements GetWeb(Guid Id)
        {
            Game game = PlayniteApi.Database.Games.Get(Id);
            GameAchievements gameAchievements = GetDefault(game);
            GenericAchievements achievementProvider = GetAchievementSource(PluginSettings.Settings, game, true);

            if (achievementProvider != null)
            {
                // Generate database only this source
                if (VerifToAddOrShow(Plugin, PlayniteApi, PluginSettings.Settings, game))
                {
                    RetroAchievements retroAchievementsProvider = achievementProvider as RetroAchievements;
                    PSNAchievements psnAchievementsProvider = achievementProvider as PSNAchievements;

                    logger.Info($"Used {achievementProvider.GetType()} for {game?.Name} - {game?.Id}");

                    if (retroAchievementsProvider != null && !SuccessStory.IsFromMenu)
                    {
                        // use a chached RetroAchievements game ID to skip retrieving that if possible
                        // TODO: store this with the game somehow so we don't need to get this from the achievements object
                        GameAchievements TEMPgameAchievements = Get(game, true);
                        ((RetroAchievements)achievementProvider).GameId = TEMPgameAchievements.RAgameID;
                    }
                    else if (retroAchievementsProvider != null)
                    {
                        ((RetroAchievements)achievementProvider).GameId = 0;
                    }


                    if (psnAchievementsProvider != null && !SuccessStory.IsFromMenu)
                    {
                        GameAchievements TEMPgameAchievements = Get(game, true);
                        ((PSNAchievements)achievementProvider).CommunicationId = TEMPgameAchievements.CommunicationId;
                    }
                    else if (psnAchievementsProvider != null)
                    {
                        ((PSNAchievements)achievementProvider).CommunicationId = null;
                    }


                    gameAchievements = achievementProvider.GetAchievements(game);


                    if (retroAchievementsProvider != null)
                    {
                        gameAchievements.RAgameID = retroAchievementsProvider.GameId;
                    }

                    Common.LogDebug(true, $"Achievements for {game.Name} - {achievementProvider.GetType()} - {Serialization.ToJson(gameAchievements)}");
                }
                else
                {
                    Common.LogDebug(true, $"VerifToAddOrShow({game.Name}, {achievementProvider.GetType()}) - KO");
                }
            }
            else
            {
                Common.LogDebug(true, $"VerifToAddOrShow({game.Name}, {achievementProvider.GetType()}) - No Achievement Client fits constraints");
            }

            // TODO: metadata update here for rarity
            gameAchievements = RefreshRarity(gameAchievements);
            gameAchievements = SetEstimateTimeToUnlock(game, gameAchievements);

            // TODO: do we need an AddOrUpdate here?

            if (!(gameAchievements?.HasAchievements ?? false))
            {
                logger.Info($"No achievements find for {game.Name} - {game.Id}");
            }
            else
            {
                logger.Info($"Find {gameAchievements.Total} achievements find for {game.Name} - {game.Id}");
            }

            return gameAchievements;
        }

        private GameAchievements RefreshRarity(GameAchievements gameAchievements)
        {
            foreach (var provider in AchievementMetadataAugmenters)
            {
                gameAchievements = provider.Value.RefreshRarity(gameAchievements);
            }

            return gameAchievements;
        }

        private GameAchievements SetEstimateTimeToUnlock(Game game, GameAchievements gameAchievements)
        {
            if (game != null && (gameAchievements?.HasAchievements ?? false))
            {
                // TODO consider adding ranking for this loop
                foreach (var Provider in AchievementProviders)
                {
                    try
                    {
                        if (Provider.Value.SetEstimateTimeToUnlock(game, gameAchievements))
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Common.LogError(ex, false, true, PluginName);
                    }
                }
            }

            return gameAchievements;
        }


        /// <summary>
        /// Get number achievements unlock by month for a game or not.
        /// </summary>
        /// <param name="GameID"></param>
        /// <returns></returns>
        public AchievementsGraphicsDataCount GetCountByMonth(Guid? GameID = null, int limit = 11)
        {
            string[] GraphicsAchievementsLabels = new string[limit + 1];
            ChartValues<CustomerForSingle> SourceAchievementsSeries = new ChartValues<CustomerForSingle>();

            LocalDateYMConverter localDateYMConverter = new LocalDateYMConverter();

            // All achievements
            if (GameID == null)
            {
                for (int i = limit; i >= 0; i--)
                {
                    GraphicsAchievementsLabels[(limit - i)] = (string)localDateYMConverter.Convert(DateTime.Now.AddMonths(-i), null, null, null);
                    SourceAchievementsSeries.Add(new CustomerForSingle
                    {
                        Name = (string)localDateYMConverter.Convert(DateTime.Now.AddMonths(-i), null, null, null),
                        Values = 0
                    });
                }

                try
                {
                    bool ShowHidden = PluginSettings.Settings.IncludeHiddenGames;
                    var db = Database.Items.Where(x => x.Value.HasAchievements && !x.Value.IsDeleted && (ShowHidden ? true : x.Value.Hidden == false)).ToList();
                    foreach (var item in db)
                    {
                        List<Achievements> temp = item.Value.Items;
                        foreach (Achievements itemAchievements in temp)
                        {
                            if (itemAchievements.DateUnlocked != null && itemAchievements.DateUnlocked != default(DateTime))
                            {
                                string tempDate = (string)localDateYMConverter.Convert(((DateTime)itemAchievements.DateUnlocked).ToLocalTime(), null, null, null);
                                int index = Array.IndexOf(GraphicsAchievementsLabels, tempDate);

                                if (index >= 0 && index < (limit + 1))
                                {
                                    SourceAchievementsSeries[index].Values += 1;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginName);
                }
            }
            // Achievement for a game
            else
            {
                try
                {
                    List<Achievements> Achievements = GetOnlyCache((Guid)GameID).Items;

                    if (Achievements != null && Achievements.Count > 0)
                    {
                        Achievements.Sort((x, y) => ((DateTime)y.DateUnlocked).CompareTo((DateTime)x.DateUnlocked));
                        DateTime TempDateTime = DateTime.Now;

                        // Find last achievement date unlock
                        if (((DateTime)Achievements[0].DateUnlocked).ToLocalTime().ToString("yyyy-MM") != "0001-01" && ((DateTime)Achievements[0].DateUnlocked).ToLocalTime().ToString("yyyy-MM") != "1982-12")
                        {
                            TempDateTime = ((DateTime)Achievements[0].DateUnlocked).ToLocalTime();
                        }

                        for (int i = limit; i >= 0; i--)
                        {
                            //GraphicsAchievementsLabels[(limit - i)] = TempDateTime.AddMonths(-i).ToString("yyyy-MM");
                            GraphicsAchievementsLabels[(limit - i)] = (string)localDateYMConverter.Convert(TempDateTime.AddMonths(-i), null, null, null);
                            SourceAchievementsSeries.Add(new CustomerForSingle
                            {
                                Name = TempDateTime.AddMonths(-i).ToString("yyyy-MM"),
                                Values = 0
                            });
                        }

                        for (int i = 0; i < Achievements.Count; i++)
                        {
                            //string tempDate = ((DateTime)Achievements[i].DateUnlocked).ToLocalTime().ToString("yyyy-MM");
                            string tempDate = (string)localDateYMConverter.Convert(((DateTime)Achievements[i].DateUnlocked).ToLocalTime(), null, null, null);
                            int index = Array.IndexOf(GraphicsAchievementsLabels, tempDate);

                            if (index >= 0 && index < (limit + 1))
                            {
                                SourceAchievementsSeries[index].Values += 1;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Error in load GetCountByMonth({GameID.ToString()})", true, PluginName);
                }
            }

            return new AchievementsGraphicsDataCount { Labels = GraphicsAchievementsLabels, Series = SourceAchievementsSeries };
        }

        // TODO: more tight coupling here
        public AchievementsGraphicsDataCountSources GetCountBySources()
        {
            List<string> tempSourcesLabels = new List<string>();
            IEnumerable<KeyValuePair<Guid, GameAchievements>> db = Database.Items.Where(x => x.Value.IsManual);

            if (PluginSettings.Settings.EnableRetroAchievementsView && PluginSettings.Settings.EnableRetroAchievements)
            {
                //TODO: _isRetroachievements this is never set
                if (_isRetroachievements)
                {
                    if (PluginSettings.Settings.EnableRetroAchievements)
                    {
                        tempSourcesLabels.Add("RetroAchievements");
                    }
                }
                else
                {
                    if (PluginSettings.Settings.EnableGog)
                    {
                        tempSourcesLabels.Add("GOG");
                    }
                    if (PluginSettings.Settings.EnableEpic)
                    {
                        tempSourcesLabels.Add("Epic");
                    }
                    if (PluginSettings.Settings.EnableSteam)
                    {
                        tempSourcesLabels.Add("Steam");
                    }
                    if (PluginSettings.Settings.EnableOrigin)
                    {
                        tempSourcesLabels.Add("EA app");
                    }
                    if (PluginSettings.Settings.EnableXbox)
                    {
                        tempSourcesLabels.Add("Xbox");
                    }
                    if (PluginSettings.Settings.EnablePsn)
                    {
                        tempSourcesLabels.Add("Playstation");
                    }
                    if (PluginSettings.Settings.EnableLocal)
                    {
                        tempSourcesLabels.Add("Playnite");
                        tempSourcesLabels.Add("Hacked");
                    }
                    if (PluginSettings.Settings.EnableRpcs3Achievements)
                    {
                        tempSourcesLabels.Add("RPCS3");
                    }
                    if (PluginSettings.Settings.EnableSc2Achievements || PluginSettings.Settings.EnableOverwatchAchievements || PluginSettings.Settings.EnableWowAchievements)
                    {
                        tempSourcesLabels.Add("Battle.net");
                    }
                    if (PluginSettings.Settings.EnableManual)
                    {
                        if (db != null && db.Count() > 0)
                        {
                            var ListSources = db.Select(x => x.Value.SourceId).Distinct();
                            foreach (var Source in ListSources)
                            {
                                var gameSource = PlayniteApi.Database.Sources.Get(Source);
                                if (gameSource != null)
                                {
                                    tempSourcesLabels.Add(gameSource.Name);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (PluginSettings.Settings.EnableGog)
                {
                    tempSourcesLabels.Add("GOG");
                }
                if (PluginSettings.Settings.EnableEpic)
                {
                    tempSourcesLabels.Add("Epic");
                }
                if (PluginSettings.Settings.EnableSteam)
                {
                    tempSourcesLabels.Add("Steam");
                }
                if (PluginSettings.Settings.EnableOrigin)
                {
                    tempSourcesLabels.Add("EA app");
                }
                if (PluginSettings.Settings.EnableXbox)
                {
                    tempSourcesLabels.Add("Xbox");
                }
                if (PluginSettings.Settings.EnablePsn)
                {
                    tempSourcesLabels.Add("Playstation");
                }
                if (PluginSettings.Settings.EnableRetroAchievements)
                {
                    tempSourcesLabels.Add("RetroAchievements");
                }
                if (PluginSettings.Settings.EnableRpcs3Achievements)
                {
                    tempSourcesLabels.Add("RPCS3");
                }
                if (PluginSettings.Settings.EnableSc2Achievements || PluginSettings.Settings.EnableOverwatchAchievements || PluginSettings.Settings.EnableWowAchievements)
                {
                    tempSourcesLabels.Add("Battle.net");
                }
                if (PluginSettings.Settings.EnableLocal)
                {
                    tempSourcesLabels.Add("Playnite");
                    tempSourcesLabels.Add("Hacked");
                }
                if (PluginSettings.Settings.EnableManual)
                {
                    if (db != null && db.Count() > 0)
                    {
                        IEnumerable<Guid> ListSources = db.Select(x => x.Value.SourceId).Distinct();
                        foreach (Guid Source in ListSources)
                        {
                            if (Source != default(Guid))
                            {
                                GameSource gameSource = PlayniteApi.Database.Sources.Get(Source);
                                if (gameSource != null)
                                {
                                    tempSourcesLabels.Add(gameSource.Name);
                                }
                            }
                        }
                    }
                }
            }

            tempSourcesLabels = tempSourcesLabels.Distinct().ToList();
            tempSourcesLabels.Sort((x, y) => x.CompareTo(y));

            string[] GraphicsAchievementsLabels = new string[tempSourcesLabels.Count];
            List<AchievementsGraphicsDataSources> tempDataUnlocked = new List<AchievementsGraphicsDataSources>();
            List<AchievementsGraphicsDataSources> tempDataLocked = new List<AchievementsGraphicsDataSources>();
            List<AchievementsGraphicsDataSources> tempDataTotal = new List<AchievementsGraphicsDataSources>();
            for (int i = 0; i < tempSourcesLabels.Count; i++)
            {
                GraphicsAchievementsLabels[i] = TransformIcon.Get(tempSourcesLabels[i]);
                tempDataLocked.Add(new AchievementsGraphicsDataSources { source = tempSourcesLabels[i], value = 0 });
                tempDataUnlocked.Add(new AchievementsGraphicsDataSources { source = tempSourcesLabels[i], value = 0 });
                tempDataTotal.Add(new AchievementsGraphicsDataSources { source = tempSourcesLabels[i], value = 0 });
            }

            bool ShowHidden = PluginSettings.Settings.IncludeHiddenGames;
            db = Database.Items.Where(x => x.Value.HasAchievements && !x.Value.IsDeleted && (ShowHidden ? true : x.Value.Hidden == false)).ToList();
            foreach (KeyValuePair<Guid, GameAchievements> item in db)
            {
                try
                {
                    string SourceName = PlayniteTools.GetSourceName(item.Key);
                    foreach (Achievements achievements in item.Value.Items)
                    {
                        for (int i = 0; i < tempDataUnlocked.Count; i++)
                        {
                            if (tempDataUnlocked[i].source.Contains(SourceName, StringComparison.InvariantCultureIgnoreCase))
                            {
                                tempDataTotal[i].value += 1;
                                if (achievements.DateUnlocked != default(DateTime))
                                {
                                    tempDataUnlocked[i].value += 1;
                                }
                                if (achievements.DateUnlocked == default(DateTime))
                                {
                                    tempDataLocked[i].value += 1;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Error on GetCountBySources() for {item.Key}", true, PluginName);
                }
            }

            ChartValues<CustomerForSingle> SourceAchievementsSeriesUnlocked = new ChartValues<CustomerForSingle>();
            ChartValues<CustomerForSingle> SourceAchievementsSeriesLocked = new ChartValues<CustomerForSingle>();
            ChartValues<CustomerForSingle> SourceAchievementsSeriesTotal = new ChartValues<CustomerForSingle>();
            for (int i = 0; i < tempDataUnlocked.Count; i++)
            {
                SourceAchievementsSeriesUnlocked.Add(new CustomerForSingle
                {
                    Name = TransformIcon.Get(tempDataUnlocked[i].source),
                    Values = tempDataUnlocked[i].value
                });
                SourceAchievementsSeriesLocked.Add(new CustomerForSingle
                {
                    Name = TransformIcon.Get(tempDataLocked[i].source),
                    Values = tempDataLocked[i].value
                });
                SourceAchievementsSeriesTotal.Add(new CustomerForSingle
                {
                    Name = TransformIcon.Get(tempDataTotal[i].source),
                    Values = tempDataTotal[i].value
                });
            }


            return new AchievementsGraphicsDataCountSources
            {
                Labels = GraphicsAchievementsLabels,
                SeriesLocked = SourceAchievementsSeriesLocked,
                SeriesUnlocked = SourceAchievementsSeriesUnlocked,
                SeriesTotal = SourceAchievementsSeriesTotal
            };
        }

        /// <summary>
        /// Get number achievements unlock by month for a game or not.
        /// </summary>
        /// <param name="GameID"></param>
        /// <returns></returns>
        public AchievementsGraphicsDataCount GetCountByDay(Guid? GameID = null, int limit = 11, bool CutPeriod = false)
        {
            string[] GraphicsAchievementsLabels = new string[limit + 1];
            ChartValues<CustomerForSingle> SourceAchievementsSeries = new ChartValues<CustomerForSingle>();

            LocalDateConverter localDateConverter = new LocalDateConverter();

            // All achievements
            if (GameID == null)
            {
                for (int i = limit; i >= 0; i--)
                {
                    GraphicsAchievementsLabels[(limit - i)] = (string)localDateConverter.Convert(DateTime.Now.AddDays(-i), null, null, null);
                    SourceAchievementsSeries.Add(new CustomerForSingle
                    {
                        Name = (string)localDateConverter.Convert(DateTime.Now.AddDays(-i), null, null, null),
                        Values = 0
                    });
                }

                try
                {
                    bool ShowHidden = PluginSettings.Settings.IncludeHiddenGames;
                    var db = Database.Items.Where(x => x.Value.HasAchievements && !x.Value.IsDeleted && (ShowHidden ? true : x.Value.Hidden == false)).ToList();
                    foreach (var item in db)
                    {
                        List<Achievements> temp = item.Value.Items;
                        foreach (Achievements itemAchievements in temp)
                        {
                            if (itemAchievements.DateWhenUnlocked != null)
                            {
                                string tempDate = (string)localDateConverter.Convert(((DateTime)itemAchievements.DateUnlocked).ToLocalTime(), null, null, null);
                                int index = Array.IndexOf(GraphicsAchievementsLabels, tempDate);

                                if (index >= 0 && index < (limit + 1))
                                {
                                    SourceAchievementsSeries[index].Values += 1;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginName);
                }
            }
            // Achievement for a game
            else
            {
                try
                {
                    List<Achievements> Achievements = GetOnlyCache((Guid)GameID).Items;

                    if (Achievements != null && Achievements.Count > 0)
                    {
                        if (CutPeriod)
                        {
                            var groupedAchievements = Achievements
                                .Where(a => a.IsUnlock && a.DateWhenUnlocked.HasValue)
                                .GroupBy(a => a.DateWhenUnlocked.Value.ToLocalTime().Date)
                                .OrderBy(g => g.Key);

                            DateTime? previousDate = null;

                            foreach (var grouping in groupedAchievements)
                            {
                                if (previousDate.HasValue && previousDate < grouping.Key.AddDays(-1))
                                {
                                    SourceAchievementsSeries.Add(new CustomerForSingle
                                    {
                                        Name = string.Empty,
                                        Values = double.NaN
                                    });
                                }
                                SourceAchievementsSeries.Add(new CustomerForSingle
                                {
                                    Name = (string)localDateConverter.Convert(grouping.Key, null, null, null),
                                    Values = grouping.Count()
                                });
                                previousDate = grouping.Key;
                            }
                            GraphicsAchievementsLabels = SourceAchievementsSeries.Select(x => x.Name).ToArray();
                        }
                        else
                        {
                            Achievements.Sort((x, y) => ((DateTime)y.DateUnlocked).CompareTo((DateTime)x.DateUnlocked));
                            DateTime TempDateTime = Achievements.Where(x => x.IsUnlock).Select(x => x.DateWhenUnlocked).Max()?.ToLocalTime() ?? DateTime.Now;

                            for (int i = limit; i >= 0; i--)
                            {
                                GraphicsAchievementsLabels[(limit - i)] = (string)localDateConverter.Convert(TempDateTime.AddDays(-i), null, null, null);

                                double DataValue = CutPeriod ? double.NaN : 0;

                                SourceAchievementsSeries.Add(new CustomerForSingle
                                {
                                    Name = (string)localDateConverter.Convert(TempDateTime.AddDays(-i), null, null, null),
                                    Values = DataValue
                                });
                            }

                            for (int i = 0; i < Achievements.Count; i++)
                            {
                                if (Achievements[i].DateWhenUnlocked != null)
                                {
                                    string tempDate = (string)localDateConverter.Convert(((DateTime)Achievements[i].DateUnlocked).ToLocalTime(), null, null, null);
                                    int index = Array.IndexOf(GraphicsAchievementsLabels, tempDate);

                                    if (index >= 0 && index < (limit + 1))
                                    {
                                        if (double.IsNaN(SourceAchievementsSeries[index].Values))
                                        {
                                            SourceAchievementsSeries[index].Values = 0;
                                        }
                                        SourceAchievementsSeries[index].Values += 1;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Error in load GetCountByDay({GameID.ToString()})", true, PluginName);
                }
            }

            return new AchievementsGraphicsDataCount { Labels = GraphicsAchievementsLabels, Series = SourceAchievementsSeries };
        }

        public static class AchievementSource
        {
            public const string Playstation       = "Playstation";
            public const string Steam             = "Steam";
            public const string GOG               = "GOG";
            public const string Epic              = "Epic";
            public const string Origin            = "Origin";
            public const string Xbox              = "Xbox";
            public const string RetroAchievements = "RetroAchievements";
            public const string RPCS3             = "RPCS3";
            public const string Overwatch         = "Overwatch";
            public const string Starcraft2        = "Starcraft2";
            public const string Wow               = "Wow";
            public const string GenshinImpact     = "GenshinImpact";
            public const string GuildWars2        = "GuildWars2";
            public const string Exophase          = "Exophase";
            public const string True              = "True";
        }

        /// <summary>
        /// Determine what providers can be used for achivements on the target game
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="game"></param>
        /// <param name="ignoreSpecial"></param>
        /// <returns></returns>
        public static GenericAchievements GetAchievementSource(SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            ExternalPlugin pluginType = PlayniteTools.GetPluginType(game.PluginId);
            return AchievementProviders
                .Select(provider => (provider: provider, rank: provider.Value.CheckAchivementSourceRank(pluginType, settings, game)))
                .Where(dr => dr.rank > 0)
                .OrderByDescending(dr => dr.rank)
                .Select(dr => dr.provider.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// Validate achievement configuration for the service this game is linked to
        /// </summary>
        /// <param name="plugin"></param>
        /// <param name="playniteApi"></param>
        /// <param name="settings"></param>
        /// <param name="game"></param>
        /// <returns>true when achievements can be retrieved for the supplied game</returns>
        public static bool VerifToAddOrShow(SuccessStory plugin, IPlayniteAPI playniteApi, SuccessStorySettings settings, Game game)
        {
            GenericAchievements achievementProvider = GetAchievementSource(settings, game);
            if (achievementProvider == null)
            {
                return false;
            }

            if (achievementProvider.EnabledInSettings())
            {
                return achievementProvider.ValidateConfiguration();
            }

            Common.LogDebug(true, $"VerifToAddOrShow() find no action for {achievementProvider.GetType()}");
            return false;
        }
        public bool VerifAchievementsLoad(Guid gameID)
        {
            return GetOnlyCache(gameID) != null;
        }


        public override void SetThemesResources(Game game)
        {
            if (game == null)
            {
                logger.Warn("game null in SetThemesResources()");
                return;
            }

            GameAchievements gameAchievements = Get(game, true);

            if (gameAchievements == null || !gameAchievements.HasData)
            {
                PluginSettings.Settings.HasData = false;

                PluginSettings.Settings.Is100Percent = false;
                PluginSettings.Settings.Unlocked = 0;
                PluginSettings.Settings.Locked = 0;
                PluginSettings.Settings.Total = 0;
                PluginSettings.Settings.Percent = 0;
                PluginSettings.Settings.EstimateTimeToUnlock = string.Empty;
                PluginSettings.Settings.ListAchievements = new List<Achievements>();

                return;
            }

            PluginSettings.Settings.HasData = gameAchievements.HasData;

            PluginSettings.Settings.Is100Percent = gameAchievements.Is100Percent;
            PluginSettings.Settings.Unlocked = gameAchievements.Unlocked;
            PluginSettings.Settings.Locked = gameAchievements.Locked;
            PluginSettings.Settings.Total = gameAchievements.Total;
            PluginSettings.Settings.Percent = gameAchievements.Progression;
            PluginSettings.Settings.EstimateTimeToUnlock = gameAchievements.EstimateTime?.EstimateTime;
            PluginSettings.Settings.ListAchievements = gameAchievements.Items;
        }

        public override void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            foreach (ItemUpdateEvent<Game> GameUpdated in e.UpdatedItems)
            {
                Database.SetGameInfo<Achievements>(PlayniteApi, GameUpdated.NewData.Id);
            }
        }


        public override void RefreshNoLoader(Guid Id)
        {
            Game game = PlayniteApi.Database.Games.Get(Id);
            GameAchievements loadedItem = Get(Id, true);
            GameAchievements webItem = null;

            if (loadedItem?.IsIgnored ?? true)
            {
                return;
            }

            logger.Info($"RefreshNoLoader({game?.Name} - {game?.Id})");

            if (loadedItem.IsManual)
            {
                GenericAchievements ProviderWithOverride = null;
                foreach (var Provider in AchievementProviders)
                {
                    if(Provider.Value.HasManualMenuOverride(PlayniteApi, PluginSettings, game))
                    {
                        ProviderWithOverride = Provider.Value;
                        break;
                    }
                }
                if (ProviderWithOverride != null)
                {
                    webItem = ProviderWithOverride.RefreshManualOverrideData(game);
                }
                else
                {
                    webItem = RefreshManual(game);
                }

                if (webItem != null)
                {
                    webItem.IsManual = true;
                    webItem = SetEstimateTimeToUnlock(game, webItem);
                    for (int i = 0; i < webItem.Items.Count; i++)
                    {
                        Achievements finded = loadedItem.Items.Find(x => (x.ApiName.IsNullOrEmpty() ? true : x.ApiName.IsEqual(webItem.Items[i].ApiName)) && x.Name.IsEqual(webItem.Items[i].Name));
                        if (finded != null)
                        {
                            webItem.Items[i].DateUnlocked = finded.DateUnlocked;
                        }
                    }
                }
            }
            else
            {
                webItem = GetWeb(Id);
            }

            bool mustUpdate = true;
            if (webItem != null && !webItem.HasAchievements)
            {
                mustUpdate = !loadedItem.HasAchievements;
            }

            if (webItem != null && !ReferenceEquals(loadedItem, webItem) && mustUpdate)
            {
                if (webItem.HasAchievements)
                {
                    webItem = SetEstimateTimeToUnlock(game, webItem);
                }
                Update(webItem);
            }
            else
            {
                webItem = loadedItem;
            }

            ActionAfterRefresh(webItem);
        }

        public override void Refresh(List<Guid> Ids)
        {
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                $"{PluginName} - {resources.GetString("LOCCommonProcessing")}",
                true
            );
            globalProgressOptions.IsIndeterminate = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                activateGlobalProgress.ProgressMaxValue = Ids.Count;

                string CancelText = string.Empty;
                foreach (Guid Id in Ids)
                {
                    if (activateGlobalProgress.CancelToken.IsCancellationRequested)
                    {
                        CancelText = " canceled";
                        break;
                    }

                    Game game = PlayniteApi.Database.Games.Get(Id);
                    string SourceName = PlayniteTools.GetSourceName(game);
                    var achievementSource = GetAchievementSource(PluginSettings.Settings, game);
                    string GameName = game.Name;
                    bool VerifToAddOrShow = SuccessStoryDatabase.VerifToAddOrShow(Plugin, PlayniteApi, PluginSettings.Settings, game);
                    GameAchievements gameAchievements = Get(game, true);

                    if (!gameAchievements.IsIgnored && VerifToAddOrShow)
                    {
                        if (VerifToAddOrShow)
                        {
                            if (!gameAchievements.IsManual)
                            {
                                RefreshNoLoader(Id);
                            }
                        }

                        activateGlobalProgress.CurrentProgressValue++;
                    }
                }

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                logger.Info($"Task Refresh(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {activateGlobalProgress.CurrentProgressValue}/{Ids.Count} items");
            }, globalProgressOptions);
        }

        public override void ActionAfterRefresh(GameAchievements item)
        {
            Game game = PlayniteApi.Database.Games.Get(item.Id);
            if ((item?.HasAchievements ?? false) && PluginSettings.Settings.AchievementFeature != null)
            {
                if (game.FeatureIds != null)
                {
                    game.FeatureIds.AddMissing(PluginSettings.Settings.AchievementFeature.Id);
                }
                else
                {
                    game.FeatureIds = new List<Guid> { PluginSettings.Settings.AchievementFeature.Id };
                }
                PlayniteApi.Database.Games.Update(game);
            }
        }

        // TODO: tight coupling here between steam and exophase, purpose of this code yet unknown
        // note this only refreshes manual games as that is what it's triggered on
        public void RefreshRaretyForAllManualOnly()
        {
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                $"{PluginName} - {resources.GetString("LOCCommonProcessing")}",
                true
            );
            globalProgressOptions.IsIndeterminate = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                IEnumerable<GameAchievements> db = Database.Where(x => x.IsManual && x.HasAchievements);
                activateGlobalProgress.ProgressMaxValue = (double)db.Count();
                string CancelText = string.Empty;

                //ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                //SteamAchievements steamAchievements = new SteamAchievements();
                //bool SteamConfig = steamAchievements.IsConfigured();

                foreach (GameAchievements gameAchievements in db)
                {
                    logger.Info($"RefreshRarety({gameAchievements.Name})");
                    if (activateGlobalProgress.CancelToken.IsCancellationRequested)
                    {
                        CancelText = " canceled";
                        break;
                    }

                    /*string SourceName = gameAchievements.SourcesLink?.Name?.ToLower();
                    switch (SourceName)
                    {
                        case "steam":
                            int.TryParse(Regex.Match(gameAchievements.SourcesLink.Url, @"\d+").Value, out int AppId);
                            if (AppId != 0)
                            {
                                if (SteamConfig)
                                {
                                    gameAchievements.Items = steamAchievements.GetGlobalAchievementPercentagesForAppByWebApi(AppId, gameAchievements.Items);
                                }
                                else
                                {
                                    logger.Warn($"No Steam config");
                                }
                            }
                            break;
                        case "exophase":
                            exophaseAchievements.SetRarety(gameAchievements, AchievementSourceOld.Local);
                            break;
                        default:
                            logger.Warn($"No sourcesLink for {gameAchievements.Name}");
                            break;
                    }*/
                    RefreshRarity(gameAchievements);

                    AddOrUpdate(gameAchievements);
                    activateGlobalProgress.CurrentProgressValue++;
                }

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                logger.Info($"Task RefreshRarety(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {activateGlobalProgress.CurrentProgressValue}/{(double)db.Count()} items");
            }, globalProgressOptions);
        }

        public void RefreshEstimateTime()
        {
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                $"{PluginName} - {resources.GetString("LOCCommonProcessing")}",
                true
            );
            globalProgressOptions.IsIndeterminate = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                IEnumerable<GameAchievements> db = Database.Where(x => x.IsManual && x.HasAchievements);
                activateGlobalProgress.ProgressMaxValue = (double)db.Count();
                string CancelText = string.Empty;

                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                SteamAchievements steamAchievements = new SteamAchievements();
                bool SteamConfig = steamAchievements.IsConfigured();

                foreach (GameAchievements gameAchievements in db)
                {
                    logger.Info($"RefreshEstimateTime({gameAchievements.Name})");

                    if (activateGlobalProgress.CancelToken.IsCancellationRequested)
                    {
                        CancelText = " canceled";
                        break;
                    }

                    Game game = PlayniteApi.Database.Games.Get(gameAchievements.Id);
                    GameAchievements gameAchievementsNew = Serialization.GetClone(gameAchievements);
                    gameAchievementsNew = SetEstimateTimeToUnlock(game, gameAchievements);
                    AddOrUpdate(gameAchievementsNew);

                    activateGlobalProgress.CurrentProgressValue++;
                }

                stopWatch.Stop();
                TimeSpan ts = stopWatch.Elapsed;
                logger.Info($"Task RefreshEstimateTime(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {activateGlobalProgress.CurrentProgressValue}/{(double)db.Count()} items");
            }, globalProgressOptions);
        }


        #region Tag system
        public override void AddTag(Game game, bool noUpdate = false)
        {
            GetPluginTags();
            GameAchievements gameAchievements = Get(game, true);

            if (gameAchievements.HasAchievements)
            {
                try
                {
                    if (gameAchievements.EstimateTime == null)
                    {
                        return;
                    }

                    Guid? TagId = FindGoodPluginTags(gameAchievements.EstimateTime.EstimateTimeMax);

                    if (TagId != null)
                    {
                        if (game.TagIds != null)
                        {
                            game.TagIds.Add((Guid)TagId);
                        }
                        else
                        {
                            game.TagIds = new List<Guid> { (Guid)TagId };
                        }

                        if (!noUpdate)
                        {
                            Application.Current.Dispatcher?.Invoke(() =>
                            {
                                PlayniteApi.Database.Games.Update(game);
                                game.OnPropertyChanged();
                            }, DispatcherPriority.Send);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, $"Tag insert error with {game.Name}", true, PluginName, string.Format(resources.GetString("LOCCommonNotificationTagError"), game.Name));
                }
            }
        }

        private Guid? FindGoodPluginTags(int EstimateTimeMax)
        {
            // Add tag
            if (EstimateTimeMax != 0)
            {
                if (EstimateTimeMax <= 1)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon0to1")}");
                }
                if (EstimateTimeMax <= 6)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon1to5")}");
                }
                if (EstimateTimeMax <= 10)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon5to10")}");
                }
                if (EstimateTimeMax <= 20)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon10to20")}");
                }
                if (EstimateTimeMax <= 30)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon20to30")}");
                }
                if (EstimateTimeMax <= 40)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon30to40")}");
                }
                if (EstimateTimeMax <= 50)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon40to50")}");
                }
                if (EstimateTimeMax <= 60)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon50to60")}");
                }
                if (EstimateTimeMax <= 70)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon60to70")}");
                }
                if (EstimateTimeMax <= 80)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon70to80")}");
                }
                if (EstimateTimeMax <= 90)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon80to90")}");
                }
                if (EstimateTimeMax <= 100)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon90to100")}");
                }
                if (EstimateTimeMax > 100)
                {
                    return CheckTagExist($"{resources.GetString("LOCCommon100plus")}");
                }
            }

            return null;
        }
        #endregion


        public void SetIgnored(GameAchievements gameAchievements)
        {
            if (!gameAchievements.IsIgnored)
            {
                Remove(gameAchievements.Id);
                GameAchievements pluginData = Get(gameAchievements.Id, true);
                pluginData.IsIgnored = true;
                AddOrUpdate(pluginData);
            }
            else
            {
                gameAchievements.IsIgnored = false;
                AddOrUpdate(gameAchievements);
                Refresh(gameAchievements.Id);
            }
        }


        public override void GetSelectData()
        {
            OptionsDownloadData View = new OptionsDownloadData(PlayniteApi);
            Window windowExtension = PlayniteUiHelper.CreateExtensionWindow(PlayniteApi, PluginName + " - " + resources.GetString("LOCCommonSelectData"), View);
            windowExtension.ShowDialog();

            List<Game> PlayniteDb = View.GetFilteredGames();
            bool OnlyMissing = View.GetOnlyMissing();

            if (PlayniteDb == null)
            {
                return;
            }

            PlayniteDb = PlayniteDb.FindAll(x => !Get(x.Id, true).IsIgnored);

            if (OnlyMissing)
            {
                PlayniteDb = PlayniteDb.FindAll(x => !Get(x.Id, true).HasData);
            }
            // Without manual
            else
            {
                PlayniteDb = PlayniteDb.FindAll(x => !Get(x.Id, true).IsManual);
            }

            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(
                $"{PluginName} - {resources.GetString("LOCCommonGettingData")}",
                true
            );
            globalProgressOptions.IsIndeterminate = false;

            PlayniteApi.Dialogs.ActivateGlobalProgress((activateGlobalProgress) =>
            {
                try
                {
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();

                    activateGlobalProgress.ProgressMaxValue = (double)PlayniteDb.Count();

                    string CancelText = string.Empty;
                    foreach (Game game in PlayniteDb)
                    {
                        if (activateGlobalProgress.CancelToken.IsCancellationRequested)
                        {
                            CancelText = " canceled";
                            break;
                        }

                        Thread.Sleep(10);

                        try
                        {
                            Get(game, false, true);
                        }
                        catch (Exception ex)
                        {
                            Common.LogError(ex, false, true, PluginName);
                        }

                        activateGlobalProgress.CurrentProgressValue++;
                    }

                    stopWatch.Stop();
                    TimeSpan ts = stopWatch.Elapsed;
                    logger.Info($"Task GetSelectData(){CancelText} - {string.Format("{0:00}:{1:00}.{2:00}", ts.Minutes, ts.Seconds, ts.Milliseconds / 10)} for {activateGlobalProgress.CurrentProgressValue}/{(double)PlayniteDb.Count()} items");
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, false, true, PluginName);
                }
            }, globalProgressOptions);
        }


        public ProgressionAchievements Progession()
        {
            ProgressionAchievements Result = new ProgressionAchievements();
            int Total = 0;
            int Locked = 0;
            int Unlocked = 0;

            try
            {
                List<KeyValuePair<Guid, GameAchievements>> db = Database.Items.Where(x => x.Value.HasAchievements).ToList();
                foreach (var item in db)
                {
                    GameAchievements GameAchievements = item.Value;
                    if (PlayniteApi.Database.Games.Get(item.Key) != null)
                    {
                        Total += GameAchievements.Total;
                        Locked += GameAchievements.Locked;
                        Unlocked += GameAchievements.Unlocked;
                    }
                    else
                    {
                        logger.Warn($"Achievements data without game for {GameAchievements.Name} & {GameAchievements.Id.ToString()}");
                    }
                }

                Result.Total = Total;
                Result.Locked = Locked;
                Result.Unlocked = Unlocked;
                Result.Progression = (Total != 0) ? (int)Math.Round((double)(Unlocked * 100 / Total)) : 0;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            return Result;
        }

        public ProgressionAchievements ProgessionLaunched()
        {
            ProgressionAchievements Result = new ProgressionAchievements();
            int Total = 1;
            int Locked = 0;
            int Unlocked = 0;

            try
            {
                List<KeyValuePair<Guid, GameAchievements>> db = Database.Items.Where(x => x.Value.Playtime > 0 && x.Value.HasAchievements).ToList();
                foreach (var item in db)
                {
                    GameAchievements GameAchievements = item.Value;
                    if (PlayniteApi.Database.Games.Get(item.Key) != null)
                    {
                        Total += GameAchievements.Total;
                        Locked += GameAchievements.Locked;
                        Unlocked += GameAchievements.Unlocked;
                    }
                    else
                    {
                        logger.Warn($"Achievements data without game for {GameAchievements.Name} & {GameAchievements.Id.ToString()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            Result.Total = Total;
            Result.Locked = Locked;
            Result.Unlocked = Unlocked;
            Result.Progression = (Total != 0) ? (int)Math.Round((double)(Unlocked * 100 / Total)) : 0;

            return Result;
        }

        public ProgressionAchievements ProgessionSource(Guid GameSourceId)
        {
            ProgressionAchievements Result = new ProgressionAchievements();
            int Total = 0;
            int Locked = 0;
            int Unlocked = 0;

            try
            {
                List<KeyValuePair<Guid, GameAchievements>> db = Database.Items.Where(x => x.Value.SourceId == GameSourceId).ToList();
                foreach (var item in db)
                {
                    Guid Id = item.Key;
                    Game Game = PlayniteApi.Database.Games.Get(Id);
                    GameAchievements GameAchievements = item.Value;

                    if (PlayniteApi.Database.Games.Get(item.Key) != null)
                    {
                        Total += GameAchievements.Total;
                        Locked += GameAchievements.Locked;
                        Unlocked += GameAchievements.Unlocked;
                    }
                    else
                    {
                        logger.Warn($"Achievements data without game for {GameAchievements.Name} & {GameAchievements.Id.ToString()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginName);
            }

            Result.Total = Total;
            Result.Locked = Locked;
            Result.Unlocked = Unlocked;
            Result.Progression = (Total != 0) ? (int)Math.Ceiling((double)(Unlocked * 100 / Total)) : 0;

            return Result;
        }
    }


    public class AchievementsGraphicsDataCount
    {
        public string[] Labels { get; set; }
        public ChartValues<CustomerForSingle> Series { get; set; }
    }

    public class AchievementsGraphicsDataCountSources
    {
        public string[] Labels { get; set; }
        public ChartValues<CustomerForSingle> SeriesUnlocked { get; set; }
        public ChartValues<CustomerForSingle> SeriesLocked { get; set; }
        public ChartValues<CustomerForSingle> SeriesTotal { get; set; }
    }

    public class AchievementsGraphicsDataSources
    {
        public string source { get; set; }
        public int value { get; set; }
    }
}
