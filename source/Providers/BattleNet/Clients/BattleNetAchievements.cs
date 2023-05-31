using CommonPlayniteShared.PluginLibrary.BattleNetLibrary.Models;
using CommonPluginsShared;
using Playnite.SDK.Data;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuccessStory.Clients
{
    abstract class BattleNetAchievements : GenericAchievements
    {
        protected string UrlOauth2      => @"https://account.blizzard.com:443/oauth2/authorization/account-settings";
        protected string UrlApiStatus   => @"https://account.blizzard.com/api/";
        protected string UrlLogin       => @"https://account.battle.net/login";






        public override void GetFilterItems(bool isRetroAchievements, Collection<ListSource> filterSourceItems)
        {
            bool retroAchievementsEnabled = PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView && PluginDatabase.PluginSettings.Settings.EnableRetroAchievements;

            if ((retroAchievementsEnabled && !isRetroAchievements) || !retroAchievementsEnabled)
            {
                if (PluginDatabase.PluginSettings.Settings.EnableOverwatchAchievements || PluginDatabase.PluginSettings.Settings.EnableSc2Achievements)
                {
                    if (!filterSourceItems.Any(dr => dr.SourceNameShort == "Battle.net"))
                    {
                        string icon = TransformIcon.Get("Battle.net") + " ";
                        filterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Battle.net", SourceNameShort = "Battle.net", IsCheck = false });
                    }
                }
            }
        }






        public BattleNetAchievements(string ClientName, string LocalLang = "", string LocalLangShort = "") : base(ClientName, LocalLang, LocalLangShort)
        {
            
        }


        #region Battle.net
        // TODO Rewrite authentification
        //protected BattleNetApiStatus GetApiStatus()
        //{
        //    try
        //    {
        //        // This refreshes authentication cookie
        //        WebViewOffscreen.NavigateAndWait(UrlOauth2);
        //        WebViewOffscreen.NavigateAndWait(UrlApiStatus);
        //        var textStatus = WebViewOffscreen.GetPageText();
        //        return Serialization.FromJson<BattleNetApiStatus>(textStatus);
        //    }
        //    catch (Exception ex)
        //    {
        //        Common.LogError(ex, false, true, PluginDatabase.PluginName);
        //        return null;
        //    }
        //}
        #endregion
    }
}
