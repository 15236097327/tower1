using MazeTD.GameServer.Entity;
using MazeTD.Shared;

namespace MazeTD.GameServer
{
    public enum CellType { Empty = 0, Blocked = 1, Spawn = 2, Base = 3 }

    public class GameGrid
    {
        public int Width { get; }
        public int Height { get; }

        private readonly CellType[,] _cells;

        public Vec2Int BasePos;
        public Vec2Int[] SpawnPositions;

        // 兼容旧代码，返回第一个出生点
        public Vec2Int SpawnPos => SpawnPositions[0];

        /* public GameGrid(int width, int height, bool centerBase = false)
         {
             Width = width;
             Height = height;
             _cells = new CellType[width, height];

             if (centerBase)
             {
                 // Alliance模式：基地在中心，四角出生
                 BasePos = new Vec2Int(width / 2, height / 2);
                 SpawnPositions = new Vec2Int[]
                 {
                     new Vec2Int(0, 0),
                     new Vec2Int(width - 1, 0),
                     new Vec2Int(0, height - 1),
                     new Vec2Int(width - 1, height - 1),
                 };
             }
             else
             {
                 // Legion模式：左上角出生，右下角基地
                 BasePos = new Vec2Int(width - 1, height - 1);
                 SpawnPositions = new Vec2Int[]
                 {
                     new Vec2Int(0, 0),
                 };
             }

             _cells[BasePos.x, BasePos.y] = CellType.Base;
             foreach (var sp in SpawnPositions)
                 _cells[sp.x, sp.y] = CellType.Spawn;
         }*/
        public GameGrid(int width, int height, bool centerBase = false)
        {
            Width = width;
            Height = height;
            _cells = new CellType[width, height];

            BasePos = new Vec2Int(width / 2, height / 2);
            _cells[BasePos.x, BasePos.y] = CellType.Base;

            if (centerBase)
            {
                // 四边中点作为出生点
                SpawnPositions = new Vec2Int[]
                {
            new Vec2Int(width / 2, 0),           // 下方
            new Vec2Int(width / 2, height - 1),  // 上方
            new Vec2Int(0, height / 2),           // 左方
            new Vec2Int(width - 1, height / 2),  // 右方
                };
            }
            else
            {
                SpawnPositions = new Vec2Int[]
                {
            new Vec2Int(0, 0),
                };
            }

            foreach (var sp in SpawnPositions)
                _cells[sp.x, sp.y] = CellType.Spawn;
        }

        public CellType GetCell(int x, int y) => _cells[x, y];

        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var c = _cells[x, y];
            return c == CellType.Empty || c == CellType.Spawn || c == CellType.Base;
        }

        /*public bool TryPlaceTower(int x, int y, AStarPathfinder pathfinder)
        {
            if (!InBounds(x, y)) return false;
            if (_cells[x, y] != CellType.Empty) return false;

            _cells[x, y] = CellType.Blocked;

            // 所有出生点到基地都必须有路
            foreach (var spawn in SpawnPositions)
            {
                var path = pathfinder.FindPath(this, spawn, BasePos);
                if (path == null || path.Count == 0)
                {
                    _cells[x, y] = CellType.Empty;
                    return false;
                }
            }
            return true;
        }*/
        public bool TryPlaceTower(int x, int y, AStarPathfinder pathfinder, List<Monster> monsters)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return false;
            if (_cells[x, y] != CellType.Empty) return false;

            _cells[x, y] = CellType.Blocked;

            bool hasPath = true;

            // 检查出生点到基地
            foreach (var spawn in SpawnPositions)
            {
                var path = pathfinder.FindPath(this, spawn, BasePos);
                if (path == null || path.Count == 0)
                {
                    hasPath = false;
                    break;
                }
            }

            // 检查所有存活怪物位置到基地
            if (hasPath)
            {
                foreach (var m in monsters.Where(m => !m.IsDead && !m.Reached))
                {
                    int mx = (int)m.X;
                    int my = (int)m.Y;
                    var path = pathfinder.FindPath(this, new Vec2Int(mx, my), BasePos);
                    if (path == null || path.Count == 0)
                    {
                        hasPath = false;
                        break;
                    }
                }
            }

            if (!hasPath)
            {
                _cells[x, y] = CellType.Empty;
                return false;
            }

            return true;
        }

        public bool TryRemoveTower(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            if (_cells[x, y] != CellType.Blocked) return false;
            _cells[x, y] = CellType.Empty;
            return true;
        }
        public void RemoveTower(int x, int y)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
                _cells[x, y] = CellType.Empty;
        }
    }
}