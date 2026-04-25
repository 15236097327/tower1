using MazeTD.Shared;

namespace MazeTD.GameServer
{
    public class AStarPathfinder
    {
        private static readonly (int dx, int dy)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
        };

        public List<Vec2Int>? FindPath(GameGrid grid, Vec2Int start, Vec2Int end)
        {
            var openSet = new PriorityQueue<Vec2Int, float>();
            var gScore = new Dictionary<(int, int), float>();
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var closedSet = new HashSet<(int, int)>();

            var startKey = (start.x, start.y);
            gScore[startKey] = 0;
            openSet.Enqueue(start, Heuristic(start, end));

            while (openSet.Count > 0)
            {
                var current = openSet.Dequeue();
                var curKey = (current.x, current.y);

                if (current.x == end.x && current.y == end.y)
                    return ReconstructPath(cameFrom, curKey, start);

                if (closedSet.Contains(curKey)) continue;
                closedSet.Add(curKey);

                foreach (var (dx, dy) in Directions)
                {
                    int nx = current.x + dx;
                    int ny = current.y + dy;

                    if (!grid.IsWalkable(nx, ny)) continue;

                    var neighborKey = (nx, ny);
                    if (closedSet.Contains(neighborKey)) continue;

                    float moveCost = 1.0f;
                    float tentativeG = gScore.GetValueOrDefault(curKey, float.MaxValue) + moveCost;

                    if (tentativeG < gScore.GetValueOrDefault(neighborKey, float.MaxValue))
                    {
                        gScore[neighborKey] = tentativeG;
                        cameFrom[neighborKey] = curKey;
                        float f = tentativeG + Heuristic(new Vec2Int(nx, ny), end);
                        openSet.Enqueue(new Vec2Int(nx, ny), f);
                    }
                }
            }
            return null;
        }

        private static float Heuristic(Vec2Int a, Vec2Int b) =>
            Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        private static List<Vec2Int> ReconstructPath(
            Dictionary<(int, int), (int, int)> cameFrom,
            (int, int) current, Vec2Int start)
        {
            var path = new List<Vec2Int>();
            while (cameFrom.ContainsKey(current))
            {
                path.Add(new Vec2Int(current.Item1, current.Item2));
                current = cameFrom[current];
            }
            path.Add(start);
            path.Reverse();
            return path;
        }
    }
}