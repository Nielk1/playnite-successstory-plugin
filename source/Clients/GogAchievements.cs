﻿using Playnite.SDK.Models;
using CommonPluginsShared;
using SuccessStory.Models;
using System;
using System.Linq;
using static CommonPluginsShared.PlayniteTools;
using CommonPluginsStores.Gog;
using System.Collections.ObjectModel;
using CommonPluginsStores.Models;
using System.Collections.Generic;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    class GogAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<AchievementSource, GenericAchievements> Providers)
        {
            Providers[AchievementSource.GOG] = new GogAchievements();
        }
    }
    class GogAchievements : GenericAchievements
    {
        public override AchievementSource GetAchievementSourceFromLibraryPlugin(ExternalPlugin pluginType, SuccessStorySettings settings, Game game)
        {
            if (pluginType == ExternalPlugin.GogLibrary && settings.EnableGog)
            {
                return AchievementSource.GOG;
            }
            return AchievementSource.None;
        }






        protected static GogApi _GogAPI;
        internal static GogApi GogAPI
        {
            get
            {
                if (_GogAPI == null)
                {
                    _GogAPI = new GogApi(PluginDatabase.PluginName);
                }
                return _GogAPI;
            }

            set => _GogAPI = value;
        }


        public GogAchievements() : base("GOG", CodeLang.GetGogLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
            TemporarySource = AchievementSource.GOG;
            GogAPI.SetLanguage(PluginDatabase.PlayniteApi.ApplicationSettings.Language);
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();

            if (IsConnected())
            {
                try
                {
                    ObservableCollection<GameAchievement> gogAchievements = GogAPI.GetAchievements(game.GameId, GogAPI.CurrentAccountInfos);    
                    if (gogAchievements?.Count > 0)
                    {
                        AllAchievements = gogAchievements.Select(x => new Achievements 
                        {
                            ApiName = x.Id,
                            Name = x.Name,
                            Description =x.Description,
                            UrlUnlocked = x.UrlUnlocked,
                            UrlLocked = x.UrlLocked,
                            DateUnlocked = x.DateUnlocked,
                            Percent = x.Percent
                        }).ToList();
                        gameAchievements.Items = AllAchievements;
                    }
                    else
                    {
                        if (!GogAPI.IsUserLoggedIn)
                        {
                            ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsGogNoAuthenticate"), ExternalPlugin.GogLibrary);
                        }
                    }
                    
                    // Set source link
                    if (gameAchievements.HasAchievements)
                    {
                        gameAchievements.SourcesLink = GogAPI.GetAchievementsSourceLink(game.Name, game.GameId, GogAPI.CurrentAccountInfos);
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
                ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsGogNoAuthenticate"), ExternalPlugin.GogLibrary);
            }

            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            if (PlayniteTools.IsDisabledPlaynitePlugins("GogLibrary"))
            {
                ShowNotificationPluginDisable(resources.GetString("LOCSuccessStoryNotificationsGogDisabled"));
                return false;
            }
            else
            {
                if (CachedConfigurationValidationResult == null)
                {
                    CachedConfigurationValidationResult = IsConnected();

                    if (!(bool)CachedConfigurationValidationResult)
                    {
                        ShowNotificationPluginNoAuthenticate(resources.GetString("LOCSuccessStoryNotificationsGogNoAuthenticate"), ExternalPlugin.GogLibrary);
                    }
                    else
                    {
                        CachedConfigurationValidationResult = IsConfigured();

                        if (!(bool)CachedConfigurationValidationResult)
                        {
                            ShowNotificationPluginNoConfiguration(resources.GetString("LOCSuccessStoryNotificationsGogBadConfig"));
                        }
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
                CachedIsConnectedResult = GogAPI.IsUserLoggedIn;
            }

            return (bool)CachedIsConnectedResult;
        }

        public override bool IsConfigured()
        {
            return IsConnected();
        }

        public override bool EnabledInSettings()
        {
            return PluginDatabase.PluginSettings.Settings.EnableGog;
        }

        public override void ResetCachedConfigurationValidationResult()
        {
            CachedConfigurationValidationResult = null;
            GogAPI.ResetIsUserLoggedIn();
        }

        public override void ResetCachedIsConnectedResult()
        {
            CachedIsConnectedResult = null;
            GogAPI.ResetIsUserLoggedIn();
        }
        #endregion
    }
}
