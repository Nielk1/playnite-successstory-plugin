using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SuccessStory.Controls
{
    /// <summary>
    /// Interaction logic for AchievmentProviderRadioButton.xaml
    /// </summary>
    public partial class AchievementProviderRadioButton : RadioButton
    {
        public AchievementProviderRadioButton()
        {
            InitializeComponent();
        }

        public string Text { get; internal set; } = "Provider";
        public string Glyph { get; internal set; } = "\uEA56";
    }
}
