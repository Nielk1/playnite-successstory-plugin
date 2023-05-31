using Playnite.SDK.Models;
using CommonPluginsShared;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Text;
using static CommonPluginsShared.PlayniteTools;
using CommonPluginsStores.Origin;
using System.Collections.ObjectModel;
using CommonPluginsStores.Models;
using System.Linq;
using static SuccessStory.Services.SuccessStoryDatabase;
using Playnite.SDK;

namespace SuccessStory.Clients
{
    class OriginAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<string, GenericAchievements> Providers, Dictionary<string, ISearchableManualAchievements> ManualSearchProviders, Dictionary<string, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            Providers[AchievementSource.Origin] = new OriginAchievements();
        }
    }
    class OriginAchievements : GenericAchievements
    {
        internal static new ILogger logger => LogManager.GetLogger();

        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game)
        {
            if (pluginType == ExternalPlugin.OriginLibrary && settings.EnableOrigin)
            {
                return 100;
            }
            return 0;
        }


        public override void GetFilterItems(bool isRetroAchievements, Collection<ListSource> filterSourceItems)
        {
            bool retroAchievementsEnabled = PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView && PluginDatabase.PluginSettings.Settings.EnableRetroAchievements;

            if ((retroAchievementsEnabled && !isRetroAchievements) || !retroAchievementsEnabled)
            {
                if (PluginDatabase.PluginSettings.Settings.EnableOrigin)
                {
                    //string icon = TransformIcon.Get("EA app") + " ";
                    //filterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "EA app", SourceNameShort = "EA app", IsCheck = false });

                    string icon = TransformIcon.Get("EA") + " ";
                    filterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Electronic Arts", SourceNameShort = "EA", IsCheck = false });
                }
            }
        }


        protected static OriginApi _OriginAPI;
        internal static OriginApi OriginAPI
        {
            get
            {
                if (_OriginAPI == null)
                {
                    _OriginAPI = new OriginApi(PluginDatabase.PluginName);
                }
                return _OriginAPI;
            }

            set => _OriginAPI = value;
        }


        public OriginAchievements() : base("EA", CodeLang.GetOriginLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language), CodeLang.GetOriginLangCountry(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
            OriginAPI.SetLanguage(PluginDatabase.PlayniteApi.ApplicationSettings.Language);
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();

            if (IsConnected())
            {
                try
                {
                    GameInfos gameInfos = OriginAPI.GetGameInfos(game.GameId, null);
                    if (gameInfos == null)
                    {
                        logger.Warn($"No gameInfos for {game.GameId}");
                        return null;
                    }

                    ObservableCollection<GameAchievement> originAchievements = OriginAPI.GetAchievements(gameInfos.Id2, OriginAPI.CurrentAccountInfos);
                    if (originAchievements?.Count > 0)
                    {
                        AllAchievements = originAchievements.Select(x => new Achievements
                        {
                            ApiName = x.Id,
                            Name = x.Name,
                            Description = x.Description,
                            UrlUnlocked = x.UrlUnlocked,
                            UrlLocked = x.UrlLocked,
                            DateUnlocked = x.DateUnlocked,
                            Percent = x.Percent
                        }).ToList();
                        gameAchievements.Items = AllAchievements;
                    }

                    // Set source link
                    if (gameAchievements.HasAchievements)
                    {
                        gameAchievements.SourcesLink = OriginAPI.GetAchievementsSourceLink(game.Name, gameInfos.Id, OriginAPI.CurrentAccountInfos);
                        gameAchievements.Handler = new MainAchievementHandler("EA", game.GameId);
                    }
                }
                catch (Exception ex)
                {
                    ShowNotificationPluginError(ex);
                    return gameAchievements;
                }
            }
            else
            {
                ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsOriginNoAuthenticate"), ExternalPlugin.OriginLibrary);
            }

            gameAchievements.SetRaretyIndicator();
            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            if (PlayniteTools.IsDisabledPlaynitePlugins("OriginLibrary"))
            {
                ShowNotificationPluginDisable(resources.GetString("LOCSuccessStoryNotificationsOriginDisabled"));
                return false;
            }
            else
            {
                if (CachedConfigurationValidationResult == null)
                {
                    CachedConfigurationValidationResult = IsConnected();

                    if (!(bool)CachedConfigurationValidationResult)
                    {
                        ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsOriginNoAuthenticate"), ExternalPlugin.OriginLibrary);
                    }
                }
                else if (!(bool)CachedConfigurationValidationResult)
                {
                    ShowNotificationPluginErrorMessage();
                }

                return (bool)CachedConfigurationValidationResult;
            }
        }

        public override bool IsConnected()
        {
            if (CachedIsConnectedResult == null)
            {
                try
                {
                    CachedIsConnectedResult = OriginAPI.IsUserLoggedIn;
                }
                catch (Exception ex)
                {
                    CachedIsConnectedResult = false;
                }
            }
            
            return (bool)CachedIsConnectedResult;
        }

        public override bool EnabledInSettings()
        {
            return PluginDatabase.PluginSettings.Settings.EnableOrigin;
        }

        public override void ResetCachedConfigurationValidationResult()
        {
            CachedConfigurationValidationResult = null;
            OriginAPI.ResetIsUserLoggedIn();
        }

        public override void ResetCachedIsConnectedResult()
        {
            CachedIsConnectedResult = null;
            OriginAPI.ResetIsUserLoggedIn();
        }
        #endregion
    }
}
