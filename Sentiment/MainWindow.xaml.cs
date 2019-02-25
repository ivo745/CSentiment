using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aylien.TextApi;
using System.Windows.Media.Animation;
using System.Timers;
using System.Windows.Media;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CSentiment
{
    public class SortedTupleCollection<TKey, TValue> : SortedSet<Tuple<TKey, TValue>> where TKey : IComparable
    {
        private class TupleComparer : Comparer<Tuple<TKey, TValue>>
        {
            public override int Compare(Tuple<TKey, TValue> x, Tuple<TKey, TValue> y)
            {
                if (x == null || y == null)
                    return 0;

                // If the keys are the same we don't care about the order.
                // Return 1 so that duplicates are not ignored.
                return x.Item1.Equals(y.Item1) ? 1 : Comparer<TKey>.Default.Compare(x.Item1, y.Item1);
            }
        }

        public SortedTupleCollection() : base(new TupleComparer()) { }

        public void Add(TKey key, TValue value)
        {
            Add(new Tuple<TKey, TValue>(key, value)); 
        }
    }

    public class CustomizedButton
    {
        public Sentiment Sentiment
        {
            get
            {
                return _sentiment;
            }
        }
        public double SentimentValue
        {
            get
            {
                return _sentimentValue;
            }
        }
        public Button Button
        {
            get
            {
                return _button;
            }
        }
        public MainWindow MainWindow { get; set; }

        private Button _button;
        private Sentiment _sentiment;
        private double _sentimentValue;
        private Storyboard _storyBoard1 = new Storyboard();
        private Storyboard _storyBoard2 = new Storyboard();
        private DoubleAnimation _expandAnimation = new DoubleAnimation();
        private DoubleAnimation _contractAnimation = new DoubleAnimation();
        private bool _isAnimating;

        private void LoadEvents()
        {
            Button.Click += button_Click;
            Button.KeyDown += button_Delete;
            Button.MouseEnter += button_Expand;
            Button.MouseLeave += button_Contract;
        }

        public void UnloadEvents()
        {
            Button.Click -= button_Click;
            Button.KeyDown -= button_Delete;
            Button.MouseEnter -= button_Expand;
            Button.MouseLeave -= button_Contract;
        }

        private double SentimentMultiplier(string polarity)
        {
            switch (polarity)
            {
                case "negative":
                    return 10;
                case "neutral":
                    return 100;
                case "positive":
                    return 1000;
            }
            return 0;
        }

        private void SetBackground(string polarity)
        {
            switch (polarity)
            {
                case "negative":
                    _button.Background = Brushes.Red;
                    break;
                case "neutral":
                    _button.Background = Brushes.Orange;
                    break;
                case "positive":
                    _button.Background = Brushes.LawnGreen;
                    break;
            }
        }

        private void SetAnimations(double width, double heigth, double delay)
        {
            // Expand animation
            _expandAnimation.To = width;
            _expandAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(delay));

            // Configure the animation to target the button's Height property.
            Storyboard.SetTargetProperty(_expandAnimation, new PropertyPath(FrameworkElement.WidthProperty));
            _storyBoard1.Children.Add(_expandAnimation);

            // Contract animation
            _contractAnimation.To = heigth;
            _contractAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(delay));

            // Configure the animation to target the button's Width property.
            Storyboard.SetTargetProperty(_contractAnimation, new PropertyPath(FrameworkElement.WidthProperty));
            _storyBoard2.Children.Add(_contractAnimation);
        }

        public CustomizedButton(Button button, Sentiment sentiment)
        {
            if (button == null || sentiment == null)
                return;

            _button = button;
            _button.Content = sentiment.Text;
            _sentiment = sentiment;
            _sentimentValue = _sentiment.PolarityConfidence * SentimentMultiplier(sentiment.Polarity);

            SetBackground(sentiment.Polarity);
            SetAnimations(150, 50, 500);

            LoadEvents();
        }

        public void UpdateProperties(int index, int row, int column)
        {
            _button.Name = "Button" + index;
            _button.SetValue(Grid.RowProperty, row);
            _button.SetValue(Grid.ColumnProperty, column);

            Storyboard.SetTargetName(_expandAnimation, _button.Name);
            Storyboard.SetTargetName(_contractAnimation, _button.Name);
        }

        public void SwapContent(bool swap, bool extended)
        {
            if (swap)
            {
                if (extended)
                    _button.Content = string.Format(CultureInfo.InvariantCulture, "Sentiment: {0} ({1}%)", _sentiment.Polarity, Math.Round(_sentiment.PolarityConfidence * 100));
                else
                    _button.Content = string.Format(CultureInfo.InvariantCulture, "{0}%", Math.Round(_sentiment.PolarityConfidence * 100));
            }
            else
                _button.Content = _sentiment.Text;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            _button.Focus();
            SwapContent(_button.Content.ToString().Equals(_sentiment.Text), true);
        }

        private void button_Delete(object sender, KeyEventArgs e)
        {
            if (e.Key.Equals(Key.Delete))
                MainWindow.DeleteLeaf(this);
        }

        private void button_Expand(object sender, MouseEventArgs e)
        {
            if (!_isAnimating)
            {
                SwapContent(!_button.Content.ToString().Equals(_sentiment.Text), true);
                _storyBoard1.Begin(_button);
                _isAnimating = true;
            }
        }

        private void button_Contract(object sender, MouseEventArgs e)
        {
            if (_isAnimating)
            {
                SwapContent(!_button.Content.ToString().Equals(_sentiment.Text), false);
                _storyBoard2.Begin(_button);
                _isAnimating = false;
            }
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Client _client;
        private static Timer _timer;
        private static bool _finishedSorting = true;
        private static bool _swap = true;
        private static bool _sortAscending = true;
        private enum treeParts { none, leaf, trunk, used }

        // Expandable lists to store sentiment percentages.
        private static List<double> _positives = new List<double>();
        private static List<double> _neutrals = new List<double>();
        private static List<double> _negatives = new List<double>();

        /// <summary>
        /// Sortable expandable Tuple bag to store sentiments and CustomizedButtons.
        /// </summary>
        private static SortedTupleCollection<double, CustomizedButton> _bag = new SortedTupleCollection<double, CustomizedButton>();

        /// <summary>
        /// Two dimensional integer array indicating the layout of the tree.
        /// </summary>
        private static int[,] _treeIndices = new int[14, 7]
        {
            {0,0,0,2,0,0,0},
            {0,0,1,2,1,0,0},
            {0,0,1,2,1,0,0},
            {0,1,0,2,0,1,0},
            {0,1,1,2,1,1,0},
            {0,0,1,2,1,0,0},
            {0,1,0,2,0,1,0},
            {1,0,1,2,1,0,1},
            {1,1,1,2,1,1,1},
            {0,1,1,2,1,1,0},
            {0,0,0,2,0,0,0},
            {0,0,0,2,0,0,0},
            {0,0,0,2,0,0,0},
            {0,0,0,2,0,0,0},
        };

        /// <summary>
        /// MainWindow initializer.
        /// </summary>
        public MainWindow()
        {
            // Default component intitializer.
            InitializeComponent();

            // Start a timer to create a parts of the tree trunk.
            int interval = 0;
            for (int i = 0; i < 14; i++)
                StartTimer(treeParts.trunk, interval += 200, null);

            // Store connection to aylien api.
            _client = new Client("0bd04e33", "824334810f3da28e0e86fa7577940672");
        }

        /// <summary>
        /// Output 3 integer values to validate avaliable index in the tree
        /// item 1: Avaliable leaf index for button mapping 
        /// Item 2: Avaliable trunk and leaf row for button placement
        /// Item 3: Avaliable trunk and leaf column for button placement
        /// </summary>
        private static Tuple<int, int, int> TreeIndex(treeParts part)
        {
            // Store index that is currently avaliable, starting from 1.
            int index = 1;

            // Iterate through every row in the two dimensional _treeIndices array.
            for (int row = 0; row < 14; row++)
            {
                // Iterate through every column in the two dimensional _treeIndices array.
                for (int column = 0; column < 7; column++)
                {
                    // Increment index for every position in the array that is in use, excluding those in the tree trunk column.
                    if (_treeIndices[row, column].Equals((int)treeParts.used) && column != 3)
                        index++;

                    // Avaliable index found.
                    if (_treeIndices[row, column].Equals((int)part))
                    {
                        // Set index to in use.
                        _treeIndices[row, column] = (int)treeParts.used;

                        // Output the data.
                        return Tuple.Create(index, row, column);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Resets the used indices by tree leafes to their default values.
        /// </summary>
        private static void ResetLeafIndices()
        {
            // Iterate through every row in the two dimensional _treeIndices array.
            for (int row = 0; row < 14; row++)
            {
                // Iterate through every column in the two dimensional _treeIndices array.
                for (int column = 0; column < 7; column++)
                {
                    // Used index found.
                    if (_treeIndices[row, column].Equals((int)treeParts.used))
                    {
                        // Skip tree trunk column.
                        if (column == 3)
                            continue;
                        else
                            // Set tree leaf index to it's default value.
                            _treeIndices[row, column] = (int)treeParts.leaf;
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a specified CustomizedButton.
        /// </summary>
        public void DeleteLeaf(CustomizedButton cb)
        {
            // When parameter cb is empty or sorting hasn't finished, terminate here.
            if (cb == null || _finishedSorting == false)
                return;

            // Set leaf index to default value.
            _treeIndices[(int)cb.Button.GetValue(Grid.RowProperty), (int)cb.Button.GetValue(Grid.ColumnProperty)] = (int)treeParts.leaf;

            // Convert SortedTupleCollection bag to a list and store it.
            List<Tuple<double, CustomizedButton>> list = _bag.ToList();

            // Remove specified tuple from the list.
            list.Remove(Tuple.Create(cb.SentimentValue, cb));

            // Re-initialize the SortedTupleCollection bag.
            _bag = new SortedTupleCollection<double, CustomizedButton>();

            // Iterate through every element in the list.
            foreach (var element in list)
            {
                // Re-add element into the SortedTupleCollection bag.
                _bag.Add(element);
            }

            // Update sentiment percentages.
            UpdateStatistics(cb.SentimentValue, cb.Sentiment.Polarity, true);

            // Unregister Button's name.
            UnregisterName(cb.Button.Name);

            cb.UnloadEvents();

            // Remove Button from the grid.
            grid.Children.Remove(cb.Button);
        }

        /// <summary>
        /// Creates a specified part of the tree.
        /// </summary>
        void CreateTreePart(treeParts part)
        {
            // Store data of avaliable tree index.
            Tuple<int, int, int> treeIndex = TreeIndex(part);

            // When treeIndex returns nothing or sorting hasn't finished, terminate here.
            if (treeIndex == null || _finishedSorting == false)
                return;

            // Create Button.
            Button button = new Button();
            button.Width = 50;
            button.Height = 25;

            // Add button to the grid.
            grid.Children.Add(button);

            switch (part)
            {
                case treeParts.leaf:
                    // Customize Button
                    CustomizedButton cb = new CustomizedButton(button, _client.Sentiment(null, TextBox.Text));
                    cb.MainWindow = this;
                    cb.UpdateProperties(treeIndex.Item1, treeIndex.Item2, treeIndex.Item3);

                    // Register button for reference.
                    RegisterName(button.Name, button);

                    // Add Button to SortedTupleCollection bag for later manipulation.
                    _bag.Add(cb.SentimentValue, cb);

                    // Update sentiment percentages.
                    UpdateStatistics(cb.SentimentValue, cb.Sentiment.Polarity, false);
                    break;
                case treeParts.trunk:
                    button.SetValue(Grid.RowProperty, treeIndex.Item2);
                    button.SetValue(Grid.ColumnProperty, treeIndex.Item3);
                    button.Background = Brushes.SandyBrown;
                    break;
            }
        }

        /// <summary>
        /// After every elapse of _trunkTimer create a part of the tree trunk.
        /// </summary>
        private void timer_Create_Trunk_Tick(object sender, EventArgs e)
        {
            // Make sure we are on the main thread.
            Application.Current.Dispatcher.Invoke(delegate
            {
                // Create a part of the tree trunk.
                CreateTreePart(treeParts.trunk);
            });
        }

        /// <summary>
        /// After every elapse of _sortTimer re-assign leaf properties.
        /// </summary>
        private void timer_Reassign_Leaf_Tick(object sender, EventArgs e, CustomizedButton cb)
        {
            // Store data of avaliable tree index.
            Tuple<int, int, int> leafIndex = TreeIndex(treeParts.leaf);

            // Make sure we are on the main thread.
            Application.Current.Dispatcher.Invoke(delegate
            {
                // Update CustomizedButton properties.
                cb.UpdateProperties(leafIndex.Item1, leafIndex.Item2, leafIndex.Item3);

                // Register name for later use.
                RegisterName(cb.Button.Name, cb.Button);

                // Add button to the grid.
                grid.Children.Add(cb.Button);
            });

            // When the maximum capacity is reached, sorting has finished.
            if (leafIndex.Item1.Equals(_bag.Count))
                _finishedSorting = true;
        }

        /// <summary>
        /// One time timer to execute timed events.
        /// </summary>
        private void StartTimer(treeParts part, int interval, CustomizedButton cb)
        {
            _timer = new Timer();
            _timer.Interval = interval += 100;
            _timer.AutoReset = false;
            if (part.Equals(treeParts.leaf))
                _timer.Elapsed += (sender, e) => timer_Reassign_Leaf_Tick(sender, e, cb);
            else
                _timer.Elapsed += timer_Create_Trunk_Tick;
            _timer.Start();
        }

        /// <summary>
        /// Update sentiment text after button addition or deletion.
        /// </summary>
        public void UpdateStatistics(double value, string polarity, bool remove)
        {
            switch (polarity)
            {
                case "negative":
                    if (remove)
                        _negatives.Remove(value * 10);
                    else
                        _negatives.Add(value * 10);
                    break;
                case "neutral":
                    if (remove)
                        _neutrals.Remove(value);
                    else
                        _neutrals.Add(value);
                    break;
                case "positive":
                    if (remove)
                        _positives.Remove(value / 10);
                    else
                        _positives.Add(value / 10);
                    break;
            }

            double percentage = 0;
            percentage = (_positives.Count / (double)_bag.Count) * 100;
            if (_positives.Count != 0)
                textBlockPositive.Text = Math.Round(percentage, 2) + "% positive" + " (" + Math.Round(_positives.Average()) + " / 100)";
            else
                textBlockPositive.Text = Math.Round(percentage, 2) + "% positive";

            percentage = (_neutrals.Count / (double)_bag.Count) * 100;
            if (_neutrals.Count != 0)
                textBlockNeutral.Text = Math.Round(percentage, 2) + "% neutral" + " (" + Math.Round(_neutrals.Average()) + " / 100)";
            else
                textBlockNeutral.Text = Math.Round(percentage, 2) + "% neutral";

            percentage = (_negatives.Count / (double)_bag.Count) * 100;
            if (_negatives.Count != 0)
                textBlockNegative.Text = Math.Round(percentage, 2) + "% negative" + " (" + Math.Round(_negatives.Average()) + " / 100)";
            else
                textBlockNegative.Text = Math.Round(percentage, 2) + "% negative";
        }

        /// <summary>
        /// Submits text entered into textbox.
        /// </summary>
        private void SubmitSentence(object sender, KeyEventArgs e)
        {
            // When keystroke 'Enter' is pressed.
            if (e.Key.Equals(Key.Enter))
            {
                // Store text typed in the textbox.
                string text = TextBox.Text;

                // When string has a value and is not blank and when sorting is finished.
                if (!string.IsNullOrWhiteSpace(text) && _finishedSorting)
                {
                    // Create a tree leaf.
                    CreateTreePart(treeParts.leaf);
                }

                // Clear text currently in the TextBox.
                TextBox.Text = "";
            }
        }

        /// <summary>
        /// Corresponding click event for the gridButton.
        /// </summary>
        private void gridButton_Click(object send, RoutedEventArgs a)
        {
            if (grid.ShowGridLines)
                grid.ShowGridLines = false;
            else
                grid.ShowGridLines = true;
        }

        /// <summary>
        /// Corresponding click event for the sortButton.
        /// </summary>
        private void sortButton_Click(object send, RoutedEventArgs a)
        {
            // When sorting is not finished or when no Tuples are stored, terminate here.
            if (_finishedSorting == false || _bag.Count == 0)
                return;

            // Sorting has just started.
            _finishedSorting = false;

            ResetLeafIndices();

            // Store interval in miliseconds for timer to elapse.
            int interval = 0;

            // Store SortedTupleCollection bag in reversed order.
            var bag = _bag.Reverse();

            // When sorting in ascending order.
            if (_sortAscending)
            {
                // SortedTupleCollection bag should not be reversed.
                bag = _bag;
                sortButton.Content = "Ascending";
                _sortAscending = false;
            }
            else
            {
                sortButton.Content = "Descending";
                _sortAscending = true;
            }

            // Iterate through every Tuple stored.
            foreach (var tuple in bag)
            {
                // Remove Button from the grid.
                grid.Children.Remove(tuple.Item2.Button);

                // Unregister Button's name.
                UnregisterName(tuple.Item2.Button.Name);

                // Start a timer to add Button again.
                StartTimer(treeParts.leaf, interval += 100, tuple.Item2);
            }
        }

        /// <summary>
        /// Corresponding click event for the swapButton.
        /// </summary>
        private void swapButton_Click(object sender, RoutedEventArgs e)
        {
            // When no Tuples are stored, terminate here.
            if (_bag.Count == 0)
                return;

            // Iterate through every Tuple stored in SortedTupleCollection bag.
            foreach (var tuple in _bag)
            {
                // Swap it's content text.
                tuple.Item2.SwapContent(_swap, false);
            }

            // Cycle through the boolean values.
            if (_swap)
                _swap = false;
            else
                _swap = true;
        }
    }
}