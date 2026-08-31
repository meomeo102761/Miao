namespace Miao.Core.TextScanning
{
    public readonly record struct AliasEntry(string NormalizedText, Guid CharacterId, Guid AliasId);
    public readonly record struct ScanMatch(int Start, int Length, Guid CharacterId, Guid AliasId);

    public sealed class AhoCorasickAutomaton
    {
        private sealed class Node
        {
            public readonly Dictionary<char, Node> Children = new();
            public Node? Fail;
            public List<AliasEntry>? Outputs;
        }

        private readonly Node _root = new();

        public static AhoCorasickAutomaton Build(IEnumerable<AliasEntry> entries)
        {
            var automaton = new AhoCorasickAutomaton();
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.NormalizedText)) continue;

                var node = automaton._root;
                foreach (var ch in entry.NormalizedText)
                {
                    if (!node.Children.TryGetValue(ch, out var next))
                    {
                        next = new Node();
                        node.Children[ch] = next;
                    }
                    node = next;
                }
                (node.Outputs ??= new()).Add(entry);
            }
            automaton.BuildFailureLinks();
            return automaton;
        }

        private void BuildFailureLinks()
        {
            var queue = new Queue<Node>();
            foreach (var child in _root.Children.Values)
            {
                child.Fail = _root;
                queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var (ch, child) in current.Children)
                {
                    var fail = current.Fail;
                    while (fail != null && !fail.Children.ContainsKey(ch))
                        fail = fail.Fail;

                    child.Fail = fail?.Children[ch] ?? _root;

                    if (child.Fail.Outputs != null)
                        (child.Outputs ??= new()).AddRange(child.Fail.Outputs);

                    queue.Enqueue(child);
                }
            }
        }

        public IReadOnlyList<ScanMatch> Search(string normalizedText, string originalTextForBoundaryCheck)
        {
            var raw = new List<ScanMatch>();
            var node = _root;

            for (int i = 0; i < normalizedText.Length; i++)
            {
                var ch = normalizedText[i];
                while (node != _root && !node.Children.ContainsKey(ch))
                    node = node.Fail!;

                node = node.Children.TryGetValue(ch, out var next) ? next : _root;

                if (node.Outputs == null) continue;

                foreach (var output in node.Outputs)
                {
                    int start = i - output.NormalizedText.Length + 1;
                    if (!IsWordBoundary(originalTextForBoundaryCheck, start, output.NormalizedText.Length))
                        continue;

                    raw.Add(new ScanMatch(start, output.NormalizedText.Length, output.CharacterId, output.AliasId));
                }
            }

            return ResolveOverlaps(raw);
        }

        private static bool IsWordBoundary(string text, int start, int length)
        {
            int end = start + length;
            bool leftOk = start == 0 || !char.IsLetter(text[start - 1]);
            bool rightOk = end >= text.Length || !char.IsLetter(text[end]);
            return leftOk && rightOk;
        }

        private static List<ScanMatch> ResolveOverlaps(List<ScanMatch> matches)
        {
            matches.Sort((a, b) => a.Start != b.Start ? a.Start - b.Start : b.Length - a.Length);

            var result = new List<ScanMatch>();
            int lastEnd = -1;
            foreach (var m in matches)
            {
                if (m.Start < lastEnd) continue;
                result.Add(m);
                lastEnd = m.Start + m.Length;
            }
            return result;
        }
    }
}