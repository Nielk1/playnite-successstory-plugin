﻿using CommonPluginsShared;
using CommonPluginsShared.Models;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SuccessStory.Models;
using System;
using System.Collections.Generic;
using System.Net;
using static CommonPluginsShared.PlayniteTools;
using static SuccessStory.Services.SuccessStoryDatabase;

namespace SuccessStory.Clients
{
    class WowAchievementsFactory : IAchievementFactory
    {
        public void BuildClient(Dictionary<AchievementSource, GenericAchievements> Providers, Dictionary<AchievementSource, ISearchableManualAchievements> ManualSearchProviders, Dictionary<AchievementSource, IMetadataAugmentAchievements> AchievementMetadataAugmenters)
        {
            Providers[AchievementSource.Wow] = new WowAchievements();
        }
    }
    internal class WowAchievements : BattleNetAchievements
    {
        public override int CheckAchivementSourceRank(ExternalPlugin pluginType, SuccessStorySettings settings, Game game, bool ignoreSpecial = false)
        {
            if (pluginType == ExternalPlugin.BattleNetLibrary)
            {
                switch (game.Name.ToLowerInvariant())
                {
                    case "wow":
                    case "world of warcraft":
                        if (settings.EnableWowAchievements)
                        {
                            return 100;
                        }
                        break;
                }
            }

            return 0;
        }




        private static string UrlWowGraphQL => @"https://worldofwarcraft.com/graphql";
        private static string UrlWowBase    => @"https://worldofwarcraft.com/{0}/character/{1}/{2}/{3}/achievements/";
        private static string UrlWowBaseLocalised { get; set; }

        private List<string> Urls { get; set; } = new List<string>();
        private string UrlWowAchCharacter           => "character/model.json";
        private string UrlWowAchPvp                 => "player-vs-player/model.json";
        private string UrlWowAchQuests              => "quests/model.json";
        private string UrlWowAchExploration         => "exploration/model.json";
        private string UrlWowAchWorlEvents          => "world-events/model.json";
        private string UrlWowAchDungeonsRaids       => "dungeons-raids/model.json";
        private string UrlWowAchProfessions         => "professions/model.json";
        private string UrlWowAchReputation          => "reputation/model.json";
        private string UrlWowAchPetBattles          => "pet-battles/model.json";
        private string UrlWowAchCollections         => "collections/model.json";
        private string UrlWowAchExpansionFeatures   => "expansion-features/model.json";
        private string UrlWowAchFeatsStrength       => "feats-of-strength/model.json";
        private string UrlWowAchLegacy              => "legacy/model.json";


        public WowAchievements() : base("Wow", CodeLang.GetEpicLang(PluginDatabase.PlayniteApi.ApplicationSettings.Language))
        {
        }


        public override GameAchievements GetAchievements(Game game)
        {
            GameAchievements gameAchievements = SuccessStory.PluginDatabase.GetDefault(game);
            List<Achievements> AllAchievements = new List<Achievements>();
            List<GameStats> AllStats = new List<GameStats>();

            string urlFinal = string.Empty;
            try
            {
                string region = PluginDatabase.PluginSettings.Settings.WowRegions.Find(x => x.IsSelected)?.Name;
                string realm = PluginDatabase.PluginSettings.Settings.WowRealms.Find(x => x.IsSelected)?.Slug;
                string character = PluginDatabase.PluginSettings.Settings.WowCharacter;

                UrlWowBaseLocalised = string.Format(UrlWowBase, LocalLang, region, realm, WebUtility.UrlEncode(character));
                Urls = new List<string>
                {
                    UrlWowAchCharacter, UrlWowAchPvp, UrlWowAchQuests, UrlWowAchExploration, UrlWowAchWorlEvents, UrlWowAchDungeonsRaids,
                    UrlWowAchProfessions, UrlWowAchReputation, UrlWowAchPetBattles, UrlWowAchCollections, UrlWowAchExpansionFeatures, UrlWowAchFeatsStrength,
                    UrlWowAchLegacy
                };

                foreach (string url in Urls)
                {
                    urlFinal = UrlWowBaseLocalised + url;
                    string data = Web.DownloadStringData(urlFinal).GetAwaiter().GetResult();
                    Serialization.TryFromJson(data, out WowAchievementsData wowAchievementsData);
                    Serialization.TryFromJson(Serialization.ToJson(wowAchievementsData?.subcategories), out dynamic subcategories);

                    if (subcategories != null)
                    {
                        foreach (var subItems in subcategories)
                        {
                            Serialization.TryFromJson(Serialization.ToJson(subItems?.Value), out SubcategoriesItem subcategoriesItem);
                            if (subcategoriesItem?.achievements != null)
                            {
                                foreach (WowAchievement wowAchievement in subcategoriesItem.achievements)
                                {
                                    AllAchievements.Add(new Achievements
                                    {
                                        Name = wowAchievement.name,
                                        Description = wowAchievement.description,
                                        UrlUnlocked = wowAchievement.icon.url,
                                        UrlLocked = string.Empty,
                                        DateUnlocked = wowAchievement.time == null ? default(DateTime) : ((DateTime)wowAchievement.time).ToLocalTime()
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, $"Error on {urlFinal}", true, PluginDatabase.PluginName);
            }


            gameAchievements.Items = AllAchievements;
            gameAchievements.ItemsStats = AllStats;


            // Set source link
            if (gameAchievements.HasAchievements)
            {
                gameAchievements.SourcesLink = new SourceLink
                {
                    GameName = "Wow",
                    Name = "Battle.net",
                    Url = UrlWowBaseLocalised
                };
            }

            // Set rarity from Exophase
            /*if (gameAchievements.HasAchievements)
            {
                ExophaseAchievements exophaseAchievements = new ExophaseAchievements();
                exophaseAchievements.SetRarety(gameAchievements, AchievementSourceOld.Overwatch);
            }*/


            return gameAchievements;
        }


        #region Configuration
        public override bool ValidateConfiguration()
        {
            return true;
        }

        public override bool EnabledInSettings()
        {
            string region = PluginDatabase.PluginSettings.Settings.WowRegions.Find(x => x.IsSelected)?.Name;
            string realm = PluginDatabase.PluginSettings.Settings.WowRealms.Find(x => x.IsSelected)?.Slug;
            string character = PluginDatabase.PluginSettings.Settings.WowCharacter;

            return (PluginDatabase.PluginSettings.Settings.EnableWowAchievements 
                && !region.IsNullOrEmpty() && !realm.IsNullOrEmpty() && !character.IsNullOrEmpty());
        }
        #endregion


        #region Wow
        public static List<CbData> GetRealm(string Region)
        {
            List<CbData> CbDatas = new List<CbData>();

            try
            {
                string payload = "{\"operationName\":\"GetRealmStatusData\",\"variables\":{\"input\":{\"compoundRegionGameVersionSlug\":\"" + Region + "\"}},\"extensions\":{\"persistedQuery\":{\"version\":1,\"sha256Hash\":\"c40d282bc48d4d686417f39ba896174eea212d3b86ba8bacd6cdf452b9111554\"}}}";
                string result = Web.PostStringDataPayload(UrlWowGraphQL, payload).GetAwaiter().GetResult();
                WowRegionResult wowRegionResult = Serialization.FromJson<WowRegionResult>(result);
                foreach(Realm realm in wowRegionResult.data.Realms)
                {
                    CbDatas.Add(new CbData { Name = realm.name, Slug = realm.slug });
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }

            return CbDatas;
        }
        #endregion
    }
}
