using System.Collections.Generic;

namespace TeamProject.Core
{
    /// <summary>
    /// Фильтр «голосования» для показаний OCR: значение принимается, только если оно
    /// встретилось не менее <see cref="RequiredVotes"/> раз среди последних <see cref="Window"/> чтений.
    /// Защищает от одиночных ошибок распознавания (например, 1234 → 1284 на одном кадре).
    /// </summary>
    public sealed class StableReading<T> where T : struct
    {
        private readonly Queue<T?> _history = new Queue<T?>();

        public StableReading(int window = 3, int requiredVotes = 2)
        {
            Window = window < 1 ? 1 : window;
            RequiredVotes = requiredVotes < 1 ? 1 : (requiredVotes > Window ? Window : requiredVotes);
        }

        public int Window { get; }
        public int RequiredVotes { get; }

        /// <summary>Последнее подтверждённое значение.</summary>
        public T? Value { get; private set; }

        /// <summary>Добавляет чтение и возвращает подтверждённое значение (или прежнее, если голосов мало).</summary>
        public T? Push(T? reading)
        {
            _history.Enqueue(reading);
            while (_history.Count > Window) _history.Dequeue();

            var counts = new Dictionary<T, int>();
            foreach (var r in _history)
            {
                if (r == null) continue;
                counts.TryGetValue(r.Value, out var c);
                counts[r.Value] = c + 1;
            }

            foreach (var kv in counts)
            {
                if (kv.Value >= RequiredVotes)
                {
                    Value = kv.Key;
                    break;
                }
            }
            return Value;
        }

        public void Reset()
        {
            _history.Clear();
            Value = null;
        }
    }
}
