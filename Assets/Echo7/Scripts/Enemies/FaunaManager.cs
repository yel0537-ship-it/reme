using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Фоновая фауна: стада рогачей (пассивные, мясо → пайки) и одиночные хищники.
    /// Ночью хищников больше, в пекло фауна уходит — меньше животных.
    /// </summary>
    public class FaunaManager : MonoBehaviour
    {
        float _timer;
        int _herdSeq;

        public void Init()
        {
            if (!Game.Config.enableFauna) return;
            for (int i = 0; i < 4; i++) SpawnHerd();
            if (Game.Tutorial == null || Game.Tutorial.AllowPredators)
                for (int i = 0; i < 2; i++) SpawnStalker();
        }

        void Update()
        {
            if (!Game.IsPlaying || !Game.Config.enableFauna) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 20f;
            int grazers = 0, stalkers = 0;
            foreach (var e in Registry.Enemies)
            {
                if (e == null || e.IsDead || e.Wave != null) continue;
                if (e.Archetype.Kind == EnemyKind.Grazer) grazers++;
                else if (e.Archetype.Kind == EnemyKind.Stalker) stalkers++;
            }
            float green = Game.Weather != null ? Game.Weather.Greenness : 1f;
            int grazerCap = Mathf.RoundToInt(Mathf.Lerp(6f, 20f, green));
            int stalkerCap = (Game.Clock.IsNight ? 5 : 2) + Game.Clock.Day / 4;
            if (grazers < grazerCap) SpawnHerd();
            if (stalkers < stalkerCap && (Game.Tutorial == null || Game.Tutorial.AllowPredators)) SpawnStalker();
        }

        Vector3 RandomWildPoint(float minDistFromBase)
        {
            var c = Game.World.Center;
            for (int i = 0; i < 30; i++)
            {
                var p = new Vector3(Random.Range(10f, WorldGenerator.WorldSize - 10f), 0, Random.Range(10f, WorldGenerator.WorldSize - 10f));
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.x, c.z)) < minDistFromBase) continue;
                var cell = Game.Grid.WorldToCell(p);
                if (Game.Grid.IsBlocked(cell.x, cell.y)) continue;
                if (Game.Player != null && (Game.Player.transform.position - p).sqrMagnitude < 40f * 40f) continue;
                return p;
            }
            return c + Vector3.forward * 100f;
        }

        void SpawnHerd()
        {
            var p = RandomWildPoint(60f);
            int n = Random.Range(3, 7);
            _herdSeq++;
            for (int i = 0; i < n; i++)
            {
                var off = Random.insideUnitCircle * 5f;
                var e = Enemy.Spawn(EnemyKind.Grazer, p + new Vector3(off.x, 0, off.y), null);
                e.Home = p;
                e.HerdId = _herdSeq;
            }
        }

        void SpawnStalker()
        {
            var p = RandomWildPoint(55f);
            var e = Enemy.Spawn(EnemyKind.Stalker, p, null);
            e.Home = p;
        }
    }
}
