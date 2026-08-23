using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Miao.UI.Services
{
    public static class PaginationHelper
    {
        public static void Build(Panel panel, int totalPages, int currentPage, Action<int> onPageSelected)
        {
            panel.Children.Clear();

            var prevBtn = new Button { Content = "‹ ", IsEnabled = currentPage > 1 };
            prevBtn.Classes.Add("pageButton");
            prevBtn.Classes.Add("pageNavButton");
            prevBtn.Click += (s, e) => onPageSelected(currentPage - 1);
            panel.Children.Add(prevBtn);

            foreach (var p in GetPageNumbersToShow(totalPages, currentPage))
            {
                if (p == -1)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "...",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    });
                    continue;
                }

                var btn = new Button { Content = p.ToString() };
                btn.Classes.Add("pageButton");
                if (p == currentPage) btn.Classes.Add("active");
                int page = p;
                btn.Click += (s, e) => onPageSelected(page);
                panel.Children.Add(btn);
            }

            var nextBtn = new Button { Content = " ›", IsEnabled = currentPage < totalPages };
            nextBtn.Classes.Add("pageButton");
            nextBtn.Classes.Add("pageNavButton");
            nextBtn.Click += (s, e) => onPageSelected(currentPage + 1);
            panel.Children.Add(nextBtn);
        }

        public static void BuildNumbersOnly(Panel panel, int totalPages, int currentPage, Action<int> onPageSelected)
        {
            panel.Children.Clear();

            foreach (var p in GetPageNumbersToShow(totalPages, currentPage))
            {
                if (p == -1)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "...",
                        Foreground = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    });
                    continue;
                }

                var btn = new Button { Content = p.ToString() };
                btn.Classes.Add("pageButton");
                if (p == currentPage) btn.Classes.Add("active");
                int page = p;
                btn.Click += (s, e) => onPageSelected(page);
                panel.Children.Add(btn);
            }
        }

        private static IEnumerable<int> GetPageNumbersToShow(int totalPages, int currentPage)
        {
            const int windowSize = 2;
            var pages = new List<int> { 1 };
            int start = Math.Max(2, currentPage - windowSize);
            int end = Math.Min(totalPages - 1, currentPage + windowSize);
            if (start > 2) pages.Add(-1);
            for (int i = start; i <= end; i++) pages.Add(i);
            if (end < totalPages - 1) pages.Add(-1);
            if (totalPages > 1) pages.Add(totalPages);
            return pages.Distinct();
        }
    }
}