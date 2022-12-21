using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuccessStory.Models
{
    public class AchievementHandler
    {
        public string Id { get; set; }
        public Dictionary<string, DateTime> Updated { get; set; }
        public AchievementHandler(string id)
        {
            this.Id = id;
        }
        //public override int GetHashCode()
        //{
        //    unchecked // disable overflow, for the unlikely possibility that you
        //    {         // are compiling with overflow-checking enabled
        //        int hash = 27;
        //        hash = (13 * hash) + Name.GetHashCode();
        //        hash = (13 * hash) + (Id ?? string.Empty).GetHashCode();
        //        return hash;
        //    }
        //}
    }
    public class MainAchievementHandler
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public DateTime Updated { get; set; }
        public MainAchievementHandler(string name, string id)
        {
            this.Name = name;
            this.Id = id;
            this.Updated = DateTime.UtcNow;
        }
        //public override int GetHashCode()
        //{
        //    unchecked // disable overflow, for the unlikely possibility that you
        //    {         // are compiling with overflow-checking enabled
        //        int hash = 27;
        //        hash = (13 * hash) + Name.GetHashCode();
        //        hash = (13 * hash) + (Id ?? string.Empty).GetHashCode();
        //        return hash;
        //    }
        //}
    }
}
