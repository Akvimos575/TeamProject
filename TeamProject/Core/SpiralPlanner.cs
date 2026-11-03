using System;
using System.Collections.Generic;

namespace TeamProject.Core
{
    public enum PlannerMove
    {
        /// <summary>Текущий сектор не дочищен — чистить.</summary>
        CleanHere,
        /// <summary>Идти по кругу вокруг базы (влево/вправо) до следующего сектора.</summary>
        Tangential,
        /// <summary>Кольцо пройдено — шаг наружу, к следующему кольцу.</summary>
        Outward,
        /// <summary>Достигнут лимит колец.</summary>
        Finished,
    }

    /// <summary>
    /// Спиральный обход вокруг базы. Начинаем с сектора базы (кольцо 0), затем шаг наружу
    /// и обход кольца по кругу. Кольцо считается пройденным, когда бот снова попал в один из первых
    /// секторов этого кольца (или прошёл заметно больше секторов, чем в нём должно быть: 6·k для
    /// шестиугольной сетки). После этого — шаг наружу и следующее кольцо.
    /// Очищенные сектора проходятся без остановки, поэтому бот всегда сначала дочищает
    /// ближайшие к базе сектора.
    /// </summary>
    public sealed class SpiralPlanner
    {
        private readonly BotState _state;
        private readonly double _cleanThreshold;
        private readonly int _maxRings;

        public SpiralPlanner(BotState state, double cleanThresholdPercent, int maxRings = 0)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _cleanThreshold = cleanThresholdPercent;
            _maxRings = maxRings;
        }

        public int Ring => _state.Ring;
        public IReadOnlyList<int> RingVisited => _state.RingVisited;
        public bool RingClosed => _state.RingClosed;

        /// <summary>Сколько секторов в кольце k на шестиугольной сетке.</summary>
        public static int ExpectedRingSize(int ring) => ring <= 0 ? 1 : 6 * ring;

        /// <summary>Минимум секторов, после которого возврат в начало кольца считается его замыканием.</summary>
        public static int MinSectorsBeforeClose(int ring) => Math.Max(3, (int)Math.Floor(ExpectedRingSize(ring) * 0.6));

        /// <summary>Аварийный предел: столько секторов в кольце точно больше, чем нужно.</summary>
        public static int MaxSectorsInRing(int ring) => (int)Math.Ceiling(ExpectedRingSize(ring) * 1.6) + 3;

        /// <summary>Бот вошёл в сектор (при движении по кольцу).</summary>
        public void OnEnterSector(int sectorId)
        {
            if (_state.Ring <= 0) return;
            var visited = _state.RingVisited;
            if (visited.Count > 0 && visited[visited.Count - 1] == sectorId) return;

            bool reenteredStart = visited.Count >= MinSectorsBeforeClose(_state.Ring)
                && (visited[0] == sectorId || (visited.Count > 1 && visited[1] == sectorId));

            visited.Add(sectorId);

            if (reenteredStart || visited.Count >= MaxSectorsInRing(_state.Ring))
                _state.RingClosed = true;
        }

        /// <summary>Решение, что делать в секторе <paramref name="sectorId"/> с процентом очистки <paramref name="cleanedPercent"/>.</summary>
        public PlannerMove Decide(int sectorId, double? cleanedPercent)
        {
            // Источник истины — HUD. Если процент не прочитан, полагаемся на сохранённый список.
            bool clean = cleanedPercent != null
                ? cleanedPercent.Value >= _cleanThreshold
                : _state.CleanedSectors.Contains(sectorId);

            if (!clean) return PlannerMove.CleanHere;

            _state.CleanedSectors.Add(sectorId);
            _state.IncompleteSectors.Remove(sectorId);

            if (_state.Ring == 0) return PlannerMove.Outward;
            if (_state.RingClosed)
            {
                if (_maxRings > 0 && _state.Ring >= _maxRings) return PlannerMove.Finished;
                return PlannerMove.Outward;
            }
            return PlannerMove.Tangential;
        }

        /// <summary>Сектор дочищен (или брошен после всех попыток).</summary>
        public void MarkCleaned(int sectorId, bool complete)
        {
            // Даже недочищенный сектор считаем пройденным, чтобы не застрять на нём навсегда.
            _state.CleanedSectors.Add(sectorId);
            if (complete) _state.IncompleteSectors.Remove(sectorId);
            else _state.IncompleteSectors.Add(sectorId);
        }

        /// <summary>Шаг наружу выполнен: бот в секторе <paramref name="sectorId"/>, начинаем новое кольцо.</summary>
        public void BeginNextRing(int sectorId)
        {
            _state.Ring++;
            _state.RingVisited = new List<int> { sectorId };
            _state.RingClosed = false;
        }

        /// <summary>Сброс к кольцу 0 (бот снова у базы, спираль заново; очищенное пропускается быстро).</summary>
        public void RestartFromHome()
        {
            _state.Ring = 0;
            _state.RingVisited = new List<int>();
            _state.RingClosed = false;
        }

        /// <summary>
        /// Желаемый угол базы относительно взгляда для движения по кругу.
        /// «Влево» считается с точки зрения игрока, смотрящего от базы наружу: при движении влево
        /// база остаётся слева (−90°), вправо — справа (+90°). Смещение <paramref name="inwardBiasDeg"/>
        /// чуть доворачивает курс к базе и компенсирует снос наружу.
        /// </summary>
        public static double TangentBaseBearing(bool circleLeft, double inwardBiasDeg)
            => circleLeft ? -90.0 + inwardBiasDeg : 90.0 - inwardBiasDeg;
    }
}
