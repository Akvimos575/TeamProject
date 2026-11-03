using System;
using TeamProject.Bot;

namespace TeamProject.Economy
{
    /// <summary>
    /// Разгрузка у упаковщика (он сам продаёт пыль). Бот уже стоит у базы; ищет упаковщик
    /// по подсказке взаимодействия («Load packager»), поворачиваясь на месте и делая короткие
    /// вылазки в 8 направлениях. Все повороты потом отменяются, чтобы не сбить отсчёт поворота.
    /// </summary>
    internal sealed class Unloader
    {
        private readonly BotContext _c;

        public Unloader(BotContext context) => _c = context;

        public bool Unload()
        {
            var k = _c.Cfg.Keys;
            if (_c.Cfg.PackBeforeUnload) _c.Input.Tap(k.Pack, 120);

            double turned = 0;
            bool found;
            if (_c.Cal.PromptRegion.IsEmpty)
            {
                Log.Warn("PromptRegion не откалиброван — нажимаю взаимодействие без проверки подсказки.");
                found = true;
            }
            else
            {
                found = SearchPackager(ref turned);
            }

            if (found)
            {
                for (int i = 0; i < Math.Max(1, _c.Cfg.InteractRepeat); i++)
                {
                    _c.Input.Hold(k.Interact, _c.Cfg.InteractHoldMs);
                    _c.Input.Sleep(350);
                }
                _c.State.LastUnloadUtc = DateTime.UtcNow;
                Log.Good("Пыль сгружена в упаковщик.");
            }
            else
            {
                Log.Warn("Упаковщик не найден рядом с меткой базы. Поставьте его ближе к метке или увеличьте PackagerSearchRings.");
            }

            // Возвращаем ориентацию, с которой пришли (net-ноль по отсчётам мыши).
            if (Math.Abs(turned) > 0.01) _c.Input.Turn(-turned);
            return found;
        }

        private bool SearchPackager(ref double turned)
        {
            if (SpinCheck(ref turned)) return true;

            var k = _c.Cfg.Keys;
            for (int ring = 1; ring <= Math.Max(0, _c.Cfg.PackagerSearchRings); ring++)
            {
                int walkMs = _c.Cfg.PackagerSearchStepMs * ring;
                for (int dir = 0; dir < 8; dir++)
                {
                    _c.Input.Hold(k.Forward, walkMs);
                    if (SpinCheck(ref turned)) return true;
                    _c.Input.Hold(k.Back, walkMs);
                    _c.Input.Turn(45);
                    turned += 45;
                }
                _c.Input.Turn(-360);
                turned -= 360;
            }
            return false;
        }

        /// <summary>Поворот на месте шагами по 30°, проверяя подсказку. При неудаче поворот отменяется.</summary>
        private bool SpinCheck(ref double turned)
        {
            if (_c.Hud.PackagerPromptVisible()) return true;
            double local = 0;
            for (int i = 0; i < 12; i++)
            {
                _c.Input.Turn(30);
                local += 30;
                _c.Input.Sleep(120);
                if (_c.Hud.PackagerPromptVisible())
                {
                    turned += local;
                    return true;
                }
            }
            _c.Input.Turn(-local);
            return false;
        }
    }
}
