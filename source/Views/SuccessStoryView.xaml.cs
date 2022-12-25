﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;
using Playnite.SDK;
using Playnite.SDK.Models;
using CommonPluginsShared;
using SuccessStory.Models;
using System.Threading.Tasks;
using SuccessStory.Services;
using CommonPluginsControls.LiveChartsCommon;
using System.Windows.Threading;
using System.Threading;
using System.Collections.ObjectModel;
using CommonPluginsShared.Extensions;
using CommonPluginsShared.Converters;
using System.Globalization;

namespace SuccessStory
{
    /// <summary>
    /// Logique d'interaction pour SuccessView.xaml
    /// </summary>
    public partial class SuccessView : UserControl
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static IResourceProvider resources = new ResourceProvider();

        private SuccessStoryDatabase PluginDatabase = SuccessStory.PluginDatabase;
        private SuccessViewData successViewData = new SuccessViewData();

        private ObservableCollection<ListSource> FilterSourceItems = new ObservableCollection<ListSource>();
        private ObservableCollection<ListViewGames> ListGames = new ObservableCollection<ListViewGames>();
        private List<string> SearchSources = new List<string>();
        private List<string> SearchStatus = new List<string>();

        private static Filters filters = null;

        private bool isRetroAchievements;

        public SuccessView(bool isRetroAchievements = false, Game GameSelected = null)
        {
            InitializeComponent();

            this.isRetroAchievements = isRetroAchievements;

            successViewData.Settings = PluginDatabase.PluginSettings.Settings;
            DataContext = successViewData;

            if (PluginDatabase.PluginSettings.Settings.UseUltraRare)
            {
                lvGameRaretyCount.Width = 350;
            }


            // sorting options
            ListviewGames.SortingDefaultDataName = PluginDatabase.PluginSettings.Settings.NameSorting;
            ListviewGames.SortingSortDirection = (PluginDatabase.PluginSettings.Settings.IsAsc) ? ListSortDirection.Ascending : ListSortDirection.Descending;
            ListviewGames.Sorting();

            // lvGames options
            if (!PluginDatabase.PluginSettings.Settings.lvGamesIcon100Percent)
            {
                lvGameIcon100Percent.Width = 0;
            }
            if (!PluginDatabase.PluginSettings.Settings.lvGamesIcon)
            {
                lvGameIcon.Width = 0;
            }
            if (!PluginDatabase.PluginSettings.Settings.lvGamesName)
            {
                lvGameName.Width = 0;
            }
            if (!PluginDatabase.PluginSettings.Settings.lvGamesLastSession)
            {
                lvGameLastActivity.Width = 0;
            }
            if (!PluginDatabase.PluginSettings.Settings.lvGamesSource)
            {
                lvGamesSource.Width = 0;
            }
            if (!PluginDatabase.PluginSettings.Settings.lvGamesProgression)
            {
                lvGameProgression.Width = 0;
            }


            ProgressionAchievements ProgressionGlobal = null;
            ProgressionAchievements ProgressionLaunched = null;

            AchievementsGraphicsDataCount GraphicsData = null;
            string[] StatsGraphicsAchievementsLabels = null;
            SeriesCollection StatsGraphicAchievementsSeries = new SeriesCollection();


            PART_DataLoad.Visibility = Visibility.Visible;
            PART_Data.Visibility = Visibility.Hidden;

            Task.Run(() =>
            {
                GetListGame();
                GetListAll();
                SetGraphicsAchievementsSources();

                ProgressionGlobal = PluginDatabase.Progession();
                ProgressionLaunched = PluginDatabase.ProgessionLaunched();

                GraphicsData = PluginDatabase.GetCountByMonth(isRetroAchievements, null, 12);
                StatsGraphicsAchievementsLabels = GraphicsData.Labels;


                string icon = string.Empty;
                if (PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView && PluginDatabase.PluginSettings.Settings.EnableRetroAchievements)
                {
                    if (!isRetroAchievements)
                    {
                        if (PluginDatabase.PluginSettings.Settings.EnableLocal)
                        {
                            icon = TransformIcon.Get("Playnite") + " ";
                            FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Playnite", SourceNameShort = "Playnite", IsCheck = false });

                            icon = TransformIcon.Get("Hacked") + " ";
                            FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Hacked", SourceNameShort = "Hacked", IsCheck = false });
                        }
                        if (PluginDatabase.PluginSettings.Settings.EnableManual)
                        {
                            icon = TransformIcon.Get("Manual Achievements") + " ";
                            FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + resources.GetString("LOCSuccessStoryManualAchievements"), SourceNameShort = resources.GetString("LOCSuccessStoryManualAchievements"), IsCheck = false });

                            PluginDatabase.Database.Items.Where(x => x.Value.IsManual && !x.Value.IsEmulators).Select(x => PlayniteTools.GetSourceName(x.Value.Game)).Distinct()
                                    .ForEach(x => 
                                    {
                                        icon = TransformIcon.Get(x) + " ";

                                        var finded = FilterSourceItems.Where(y => y.SourceNameShort.IsEqual(x)).FirstOrDefault();
                                        if (finded == null)
                                        {
                                            FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + x, SourceNameShort = x, IsCheck = false });
                                        }
                                    });
                        }
                    }
                }
                else
                {
                    if (PluginDatabase.PluginSettings.Settings.EnableLocal)
                    {
                        icon = TransformIcon.Get("Playnite") + " ";
                        FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Playnite", SourceNameShort = "Playnite", IsCheck = false });

                        icon = TransformIcon.Get("Hacked") + " ";
                        FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + "Hacked", SourceNameShort = "Hacked", IsCheck = false });
                    }
                    if (PluginDatabase.PluginSettings.Settings.EnableManual)
                    {
                        icon = TransformIcon.Get("Manual Achievements") + " ";
                        FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + resources.GetString("LOCSuccessStoryManualAchievements"), SourceNameShort = resources.GetString("LOCSuccessStoryManualAchievements"), IsCheck = false });

                        PluginDatabase.Database.Items.Where(x => x.Value.IsManual).Select(x => PlayniteTools.GetSourceName(x.Value.Game)).Distinct()
                                .ForEach(x =>
                                {
                                    icon = TransformIcon.Get(x) + " ";

                                    var finded = FilterSourceItems.Where(y => y.SourceNameShort.IsEqual(x)).FirstOrDefault();
                                    if (finded == null)
                                    {
                                        FilterSourceItems.Add(new ListSource { SourceName = ((icon.Length == 2) ? icon : string.Empty) + x, SourceNameShort = x, IsCheck = false });
                                    }
                                });
                    }
                }
                foreach (var provider in SuccessStoryDatabase.AchievementProviders)
                {
                    provider.Value.GetFilterItems(isRetroAchievements, FilterSourceItems);
                }
            })
            .ContinueWith(antecedent =>
            {
                this.Dispatcher?.BeginInvoke(DispatcherPriority.Loaded, new ThreadStart(delegate
                {
                    GraphicTitle.Content = string.Empty;
                    GraphicTitleALL.Content = resources.GetString("LOCSuccessStoryGraphicTitleALL");

                    FilterSourceItems = FilterSourceItems.OrderBy(x => x.SourceNameShort).ToObservable();
                    successViewData.FilterSourceItems = FilterSourceItems;

                    successViewData.ListGames = ListGames;
                    successViewData.TotalFoundCount = ListGames.Count;
                    ListviewGames.Sorting();

                    PART_TotalCommun.Content = successViewData.ListGames.Select(x => x.Common.UnLocked).Sum();
                    PART_TotalNoCommun.Content = successViewData.ListGames.Select(x => x.NoCommon.UnLocked).Sum();
                    PART_TotalRare.Content = successViewData.ListGames.Select(x => x.Rare.UnLocked).Sum();
                    PART_TotalUltraRare.Content = successViewData.ListGames.Select(x => x.UltraRare.UnLocked).Sum();


                    if (PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView && PluginDatabase.PluginSettings.Settings.EnableRetroAchievements && isRetroAchievements)
                    {
                        PART_GraphicBySource.Visibility = Visibility.Collapsed;
                        Grid.SetColumn(PART_GraphicAllUnlocked, 0);
                        Grid.SetColumnSpan(PART_GraphicAllUnlocked, 3);
                    }


                    successViewData.ProgressionGlobalCountValue = ProgressionGlobal.Unlocked;
                    successViewData.ProgressionGlobalCountMax = ProgressionGlobal.Total;
                    successViewData.ProgressionGlobal = ProgressionGlobal.Progression + "%";

                    successViewData.ProgressionLaunchedCountValue = ProgressionLaunched.Unlocked;
                    successViewData.ProgressionLaunchedCountMax = ProgressionLaunched.Total;
                    successViewData.ProgressionLaunched = ProgressionLaunched.Progression + "%";


                    StatsGraphicAchievementsSeries.Add(new LineSeries
                    {
                        Title = string.Empty,
                        Values = GraphicsData.Series
                    });
                    AchievementsMonth.Series = StatsGraphicAchievementsSeries;
                    AchievementsMonthX.Labels = StatsGraphicsAchievementsLabels;


                    // Set game selected
                    if (GameSelected != null)
                    {
                        ListviewGames.SelectedIndex = ListGames.IndexOf(ListGames.Where(x => x.Name == GameSelected.Name).FirstOrDefault());
                    }
                    ListviewGames.ScrollIntoView(ListviewGames.SelectedItem);


                    if (filters != null)
                    {
                        PART_DatePicker.SelectedDate = filters.FilterDate;
                        PART_FilteredGames.IsChecked = filters.FilteredGames;
                        PART_FilterRange.UpperValue = filters.FilterRangeMax;
                        PART_FilterRange.LowerValue = filters.FilterRangeMin;
                        TextboxSearch.Text = filters.SearchText;

                        SearchSources = filters.SearchSources;
                        if (SearchSources.Count != 0)
                        {
                            FilterSource.Text = string.Join(", ", SearchSources);
                        }

                        SearchStatus = filters.SearchStatus;
                        if (SearchStatus.Count != 0)
                        {
                            FilterStatus.Text = string.Join(", ", SearchStatus);
                        }

                        filters = null;
                        Filter();
                    }


                    PART_DataLoad.Visibility = Visibility.Hidden;
                    PART_Data.Visibility = Visibility.Visible;
                }));
            });


            if (!PluginDatabase.PluginSettings.Settings.DisplayChart)
            {
                PART_Chart1.Visibility = Visibility.Collapsed;
                Grid.SetRow(PART_PluginListContener, 2);

                PART_GraphicBySource.Visibility = Visibility.Collapsed;
                PART_GraphicAllUnlocked.Visibility = Visibility.Collapsed;
                Grid.SetRowSpan(PART_PluginListContener, 5);
                Grid.SetRowSpan(PART_GridContenerLv, 5);
            }


            successViewData.FilterStatusItems = API.Instance.Database.CompletionStatuses.Select(x => new ListStatus { StatusName = x.Name }).ToObservable();
        }

        private void SetGraphicsAchievementsSources()
        {
            var data = PluginDatabase.GetCountBySources(isRetroAchievements);

            this.Dispatcher.BeginInvoke((Action)delegate
            {
                //let create a mapper so LiveCharts know how to plot our CustomerViewModel class
                var customerVmMapper = Mappers.Xy<CustomerForSingle>()
                    .X((value, index) => index)
                    .Y(value => value.Values);

                //lets save the mapper globally
                Charting.For<CustomerForSingle>(customerVmMapper);

                SeriesCollection StatsGraphicAchievementsSeries = new SeriesCollection();
                StatsGraphicAchievementsSeries.Add(new ColumnSeries
                {
                    Title = string.Empty,
                    Values = data.SeriesUnlocked
                });

                StatsGraphicAchievementsSources.Series = StatsGraphicAchievementsSeries;
                StatsGraphicAchievementsSourcesX.Labels = data.Labels;
            });
        }

        /// <summary>
        /// Show list game with achievement.
        /// </summary>
        public void GetListGame()
        {
            try
            {
                string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                bool ShowHidden = PluginDatabase.PluginSettings.Settings.IncludeHiddenGames;

                RelayCommand<Guid> GoToGame = new RelayCommand<Guid>((Id) =>
                {
                    SuccessView.filters = new Filters
                    {
                        FilterDate = PART_DatePicker.SelectedDate,
                        FilteredGames = (bool)PART_FilteredGames.IsChecked,
                        FilterRangeMax = PART_FilterRange.UpperValue,
                        FilterRangeMin = PART_FilterRange.LowerValue,
                        SearchSources = SearchSources,
                        SearchStatus = SearchStatus,
                        SearchText = TextboxSearch.Text
                    };

                    API.Instance.MainView.SelectGame(Id);
                    API.Instance.MainView.SwitchToLibraryView();
                });


                ListGames = PluginDatabase.Database
                    .Where(x => x.HasAchievements && !x.IsDeleted && (ShowHidden ? true : x.Hidden == false)
                     && (PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView ? (isRetroAchievements ? PlayniteTools.IsGameEmulated(x.Game) : !PlayniteTools.IsGameEmulated(x.Game)) : true)
                    )
                    .Select(x => new ListViewGames
                    {
                        GoToGame = GoToGame,

                        Icon100Percent = x.Is100Percent ? Path.Combine(pluginFolder, "Resources\\badge.png") : string.Empty,
                        Id = x.Id.ToString(),
                        Name = x.Name,
                        CompletionStatus = x.Game?.CompletionStatus?.Name ?? string.Empty,
                        Icon = !x.Icon.IsNullOrEmpty() ? PluginDatabase.PlayniteApi.Database.GetFullFilePath(x.Icon) : string.Empty,
                        LastActivity = x.LastActivity?.ToLocalTime(),
                        SourceName = PlayniteTools.GetSourceName(x.Id),
                        SourceIcon = TransformIcon.Get(PlayniteTools.GetSourceName(x.Id)),
                        ProgressionValue = x.Progression,
                        Total = x.Total,
                        TotalPercent = x.Progression + "%",
                        Unlocked = x.Unlocked,
                        IsManual = x.IsManual,

                        FirstUnlock = x.FirstUnlock,
                        LastUnlock = x.LastUnlock,
                        DatesUnlock = x.DatesUnlock,

                        Common = x.Common,
                        NoCommon = x.NoCommon,
                        Rare = x.Rare,
                        UltraRare = x.UltraRare
                    }).ToObservable();
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }
        public void GetListAll()
        {
            try
            {
                string pluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                ObservableCollection<ListAll> ListAll = new ObservableCollection<ListAll>();
                PluginDatabase.Database.Where(x => x.HasAchievements && !x.IsDeleted
                     && (PluginDatabase.PluginSettings.Settings.EnableRetroAchievementsView ? (isRetroAchievements ? PlayniteTools.IsGameEmulated(x.Game) : !PlayniteTools.IsGameEmulated(x.Game)) : true)
                    )
                        .ForEach(x =>
                        {
                            x.Items.Where(y => y.IsUnlock).ForEach(y => 
                            {
                                ListAll.Add(new ListAll
                                {
                                    Id = x.Id.ToString(),
                                    Name = x.Name,
                                    Icon = !x.Icon.IsNullOrEmpty() ? PluginDatabase.PlayniteApi.Database.GetFullFilePath(x.Icon) : string.Empty,
                                    LastActivity = x.LastActivity?.ToLocalTime(),
                                    SourceName = PlayniteTools.GetSourceName(x.Id),
                                    SourceIcon = TransformIcon.Get(PlayniteTools.GetSourceName(x.Id)),
                                    IsManual = x.IsManual,

                                    FirstUnlock = x.FirstUnlock,
                                    LastUnlock = x.LastUnlock,
                                    DatesUnlock = x.DatesUnlock,

                                    AchIcon = y.Icon,
                                    AchIsGray = y.IsGray,
                                    AchEnableRaretyIndicator = PluginDatabase.PluginSettings.Settings.EnableRaretyIndicator,
                                    AchDisplayRaretyValue = PluginDatabase.PluginSettings.Settings.EnableRaretyIndicator,
                                    AchName = y.Name,
                                    AchDateUnlock = y.DateUnlocked,
                                    AchDescription = y.Description,
                                    AchPercent = y.Percent
                                });
                            });
                        });

                successViewData.ListAll = ListAll;
            }
            catch (Exception ex)
            {
                Common.LogError(ex, false, true, PluginDatabase.PluginName);
            }
        }

        /// <summary>
        /// Show Achievements for the selected game.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ListviewGames_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListViewGames GameSelected = (ListViewGames)((ListBox)sender).SelectedItem;
            if (GameSelected != null)
            {
                GraphicTitle.Content = resources.GetString("LOCSuccessStoryGraphicTitleDay");

                Guid GameId = Guid.Parse(GameSelected.Id);
                successViewData.GameContext = PluginDatabase.PlayniteApi.Database.Games.Get(GameId);
            }
            else
            {
                successViewData.GameContext = null;
            }
        }


        #region Filter
        private void Filter()
        {
            double Min = PART_FilterRange.LowerValue;
            double Max = PART_FilterRange.UpperValue;

            bool OnlyFilteredGames = (bool)PART_FilteredGames.IsChecked;

            DateTime dateStart = default(DateTime);
            DateTime dateEnd = default(DateTime);
            if (!PART_TextDate.Text.IsNullOrEmpty())
            {
                dateStart = (DateTime)PART_DatePicker.SelectedDate;
                dateEnd = new DateTime(dateStart.Year, dateStart.Month, DateTime.DaysInMonth(dateStart.Year, dateStart.Month));
            }

            //bool IsManual = false;
            //if (SearchSources.Contains(resources.GetString("LOCSuccessStoryManualAchievements")))
            //{
            //    IsManual = true;
            //    SearchSources.Remove(resources.GetString("LOCSuccessStoryManualAchievements"));
            //}
            bool IsManual = SearchSources.Contains(resources.GetString("LOCSuccessStoryManualAchievements"));

            successViewData.ListGames = ListGames.Where(x => CheckData(x, Min, Max, dateStart, dateEnd, IsManual, OnlyFilteredGames)).Distinct().ToObservable();

            successViewData.TotalFoundCount = successViewData.ListGames.Count;
            ListviewGames.Sorting();
            ListviewGames.SelectedIndex = -1;

            PART_TotalCommun.Content = successViewData.ListGames.Select(x => x.Common.UnLocked).Sum();
            PART_TotalNoCommun.Content = successViewData.ListGames.Select(x => x.NoCommon.UnLocked).Sum();
            PART_TotalRare.Content = successViewData.ListGames.Select(x => x.Rare.UnLocked).Sum();
            PART_TotalUltraRare.Content = successViewData.ListGames.Select(x => x.UltraRare.UnLocked).Sum();
        }

        private bool CheckData(ListViewGames listViewGames, double Min, double Max, DateTime dateStart, DateTime dateEnd, bool IsManual, bool OnlyFilteredGames)
        {
            bool aa = listViewGames.ProgressionValue >= Min;
            bool bb = listViewGames.ProgressionValue <= Max;
            bool cc = !TextboxSearch.Text.IsNullOrEmpty() ? listViewGames.Name.RemoveDiacritics().Contains(TextboxSearch.Text.RemoveDiacritics(), StringComparison.InvariantCultureIgnoreCase) : true;
            bool dd = !PART_TextDate.Text.IsNullOrEmpty() ? listViewGames.DatesUnlock.Any(y => y >= dateStart && y <= dateEnd) : true;
            bool ee = SearchSources.Where(dr => dr != resources.GetString("LOCSuccessStoryManualAchievements")).Any() ? SearchSources.Contains(listViewGames.SourceName, StringComparer.InvariantCultureIgnoreCase) : true;
            bool gg = IsManual ? listViewGames.IsManual : true;
            bool hh = OnlyFilteredGames ? API.Instance.MainView.FilteredGames.Find(y => y.Id.ToString().IsEqual(listViewGames.Id)) != null : true;
            bool ii = SearchStatus.Count != 0 ? SearchStatus.Contains(listViewGames.CompletionStatus, StringComparer.InvariantCultureIgnoreCase) : true;

            bool ff = aa && bb && cc && dd && ee && gg && hh && ii;
            return ff;
        }

        private void TextboxSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            Filter();
        }

        private void ChkSource_Checked(object sender, RoutedEventArgs e)
        {
            FilterCbSource((CheckBox)sender);
        }

        private void ChkSource_Unchecked(object sender, RoutedEventArgs e)
        {
            FilterCbSource((CheckBox)sender);
        }

        private void FilterCbSource(CheckBox sender)
        {
            FilterSource.Text = string.Empty;

            if ((bool)sender.IsChecked)
            {
                SearchSources.Add((string)sender.Tag);
            }
            else
            {
                SearchSources.Remove((string)sender.Tag);
            }

            if (SearchSources.Count != 0)
            {
                FilterSource.Text = string.Join(", ", SearchSources);
            }

            Filter();
        }

        private void Chkstatus_Checked(object sender, RoutedEventArgs e)
        {
            FilterCbStatus((CheckBox)sender);
        }

        private void Chkstatus_Unchecked(object sender, RoutedEventArgs e)
        {
            FilterCbStatus((CheckBox)sender);
        }

        private void FilterCbStatus(CheckBox sender)
        {
            FilterStatus.Text = string.Empty;

            if ((bool)sender.IsChecked)
            {
                SearchStatus.Add((string)sender.Tag);
            }
            else
            {
                SearchStatus.Remove((string)sender.Tag);
            }

            if (SearchStatus.Count != 0)
            {
                FilterStatus.Text = string.Join(", ", SearchStatus);
            }

            Filter();
        }

        private void RangeSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            Filter();
        }

        private void PART_FilteredGames_Click(object sender, RoutedEventArgs e)
        {
            Filter();
        }


        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            DatePicker control = sender as DatePicker;
            DateTime dateNew = (DateTime)control.SelectedDate;

            LocalDateYMConverter localDateYMConverter = new LocalDateYMConverter();
            PART_TextDate.Text = localDateYMConverter.Convert(dateNew, null, null, CultureInfo.CurrentCulture).ToString();
            Filter();
        }
        private void PART_ClearButton_Click(object sender, RoutedEventArgs e)
        {
            PART_TextDate.Text = string.Empty;
            Filter();
        }
        #endregion
    }


    public class SuccessViewData : ObservableObject
    {
        private ObservableCollection<ListViewGames> _ListGames = new ObservableCollection<ListViewGames>();
        public ObservableCollection<ListViewGames> ListGames
        {
            get => _ListGames;
            set => SetValue(ref _ListGames, value);
        }

        private ObservableCollection<ListAll> _ListAll = new ObservableCollection<ListAll>();
        public ObservableCollection<ListAll> ListAll
        {
            get => _ListAll; 
            set => SetValue(ref _ListAll, value);
        }

        private ObservableCollection<ListSource> _FilterSourceItems = new ObservableCollection<ListSource>();
        public ObservableCollection<ListSource> FilterSourceItems
        {
            get => _FilterSourceItems;
            set => SetValue(ref _FilterSourceItems, value);
        }

        private ObservableCollection<ListStatus> _FilterStatusItems = new ObservableCollection<ListStatus>();
        public ObservableCollection<ListStatus> FilterStatusItems
        {
            get => _FilterStatusItems;
            set => SetValue(ref _FilterStatusItems, value);
        }

        private int _TotalFoundCount = 100;
        public int TotalFoundCount
        {
            get => _TotalFoundCount;
            set => SetValue(ref _TotalFoundCount, value);
        }

        private int _ProgressionGlobalCountValue = 20;
        public int ProgressionGlobalCountValue
        {
            get => _ProgressionGlobalCountValue;
            set => SetValue(ref _ProgressionGlobalCountValue, value);
        }

        private int _ProgressionGlobalCountMax= 100;
        public int ProgressionGlobalCountMax
        {
            get => _ProgressionGlobalCountMax;
            set => SetValue(ref _ProgressionGlobalCountMax, value);
        }

        private string _ProgressionGlobal = "20%";
        public string ProgressionGlobal
        {
            get => _ProgressionGlobal;
            set => SetValue(ref _ProgressionGlobal, value);
        }

        private int _ProgressionLaunchedCountValue = 40;
        public int ProgressionLaunchedCountValue
        {
            get => _ProgressionLaunchedCountValue;
            set => SetValue(ref _ProgressionLaunchedCountValue, value);
        }

        private int _ProgressionLaunchedCountMax = 100;
        public int ProgressionLaunchedCountMax
        {
            get => _ProgressionLaunchedCountMax;
            set => SetValue(ref _ProgressionLaunchedCountMax, value);
        }

        private string _ProgressionLaunched = "40%";
        public string ProgressionLaunched
        {
            get => _ProgressionLaunched;
            set => SetValue(ref _ProgressionLaunched, value);
        }

        private Game _GameContext;
        public Game GameContext
        {
            get => _GameContext;
            set => SetValue(ref _GameContext, value);
        }

        private SuccessStorySettings _Settings;
        public SuccessStorySettings Settings
        {
            get => _Settings;
            set => SetValue(ref _Settings, value);
        }
    }


    public class ListSource
    {
        public string SourceName { get; set; }
        public string SourceNameShort { get; set; }
        public bool IsCheck { get; set; }
    }

    public class ListStatus
    {
        public string StatusName { get; set; }
        public bool IsCheck { get; set; }
    }


    public class Filters
    {
        public string SearchText { get; set; } = string.Empty;
        public List<string> SearchSources { get; set; } = new List<string>();
        public List<string> SearchStatus { get; set; } = new List<string>();
        public double FilterRangeMin { get; set; } = 0;
        public double FilterRangeMax { get; set; } = 100;
        public DateTime? FilterDate { get; set; } = null; 
        public bool FilteredGames { get; set; } = false; 
    }
}
