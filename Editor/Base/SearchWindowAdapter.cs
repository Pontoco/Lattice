using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Searcher;

namespace Lattice.Editor
{
    /// <summary>Adaptor that prioritises search results. Same as ShaderGraph's implementation.</summary>
    internal sealed class SearchWindowAdapter : SearcherAdapter
    {
        public override bool HasDetailsPanel => false;

        public SearchWindowAdapter(string title) : base(title) { }

        private static SearcherItem GetFirstChildItem(SearcherItem item)
        {
            if (item.Children.Count == 0)
            {
                return item;
            }

            SearcherItem childIterator = item.Children[0];

            // Discard searcher item for selection if it is a category, get next best child item from it instead
            // There is no utility in selecting category headers/titles, only the leaf entries
            while (childIterator != null && childIterator.Children.Count != 0)
            {
                childIterator = childIterator.Children[0];
            }

            item = childIterator;

            return item;
        }

        private static int ComputeScoreForMatch(string[] queryTerms, SearcherItem matchItem)
        {
            // Scoring Criteria:
            // - Exact name match is most preferred.
            // - Partial name match is next.
            // - Exact synonym match is next.
            // - Partial synonym match is next.
            // - No match is last.
            int score = 0;

            // Split the entry name so that we can remove suffix that looks like "Clamp: In(4)"
            string nameSansSuffix = matchItem.Name.Split(':').First();

            int nameCharactersMatched = 0;

            foreach (string queryWord in queryTerms)
            {
                if (nameSansSuffix.Contains(queryWord, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100000;
                    nameCharactersMatched += queryWord.Length;
                }

                if (matchItem.Synonyms == null)
                {
                    continue;
                }

                // Check for synonym matches -- give a bonus to each
                foreach (string syn in matchItem.Synonyms)
                {
                    if (syn.Equals(queryWord, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 10000;
                    }
                    else if (syn.Contains(queryWord, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 1000;
                        score -= syn.Length - queryWord.Length;
                    }
                }
            }

            if (nameCharactersMatched > 0)
            {
                int unmatchedCharacters = nameSansSuffix.Length - nameCharactersMatched;
                score -= unmatchedCharacters;
            }

            return score;
        }

        public override SearcherItem OnSearchResultsFilter(IEnumerable<SearcherItem> searchResults, string searchQuery)
        {
            if (searchQuery.Length == 0)
            {
                return GetFirstChildItem(searchResults.FirstOrDefault());
            }

            // Sort results by length so that shorter length results are prioritized
            // prevents entries with short names getting stuck at end of list after entries with longer names when both contain the same word
            searchResults = searchResults.OrderBy(x => x.Name.Length).ToList();

            SearcherItem bestMatch = GetFirstChildItem(searchResults.FirstOrDefault());
            int bestScore = 0;
            List<int> visitedItems = new();
            string[] queryTerms = searchQuery.Split(' ');
            foreach (SearcherItem result in searchResults)
            {
                SearcherItem currentItem = GetFirstChildItem(result);

                if (currentItem.Parent == null)
                {
                    continue;
                }

                SearcherItem parentItem = currentItem.Parent;
                foreach (SearcherItem matchItem in parentItem.Children)
                {
                    if (visitedItems.Contains(matchItem.Id))
                    {
                        continue;
                    }

                    int currentScore = ComputeScoreForMatch(queryTerms, matchItem);
                    if (currentScore > bestScore)
                    {
                        bestScore = currentScore;
                        bestMatch = matchItem;
                    }

                    visitedItems.Add(matchItem.Id);
                }
            }

            return bestMatch;
        }
    }
}
