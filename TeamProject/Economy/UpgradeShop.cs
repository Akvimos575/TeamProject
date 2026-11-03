using System;
using System.Collections.Generic;
using TeamProject.Bot;
using TeamProject.Core;

namespace TeamProject.Economy
{
    /// <summary>
    /// Покупка улучшений по списку приоритетов из config.json. Каждая покупка — записанный макрос
    /// (открыть меню, выбрать вкладку, нажать «купить», закрыть меню). Деньги читаются OCR до и после,
    /// чтобы подтвердить покупку.
    /// </summary>
    internal sealed class UpgradeShop
    {
        private readonly BotContext _c;
        private readonly HashSet<string> _warned = new HashSet<string>();

        public UpgradeShop(BotContext context) => _c = context;

        public void TryBuy()
        {
            var cfg = _c.Cfg;
            if (!cfg.ShopEnabled || cfg.Upgrades.Count == 0) return;

            var money = _c.Hud.ReadMoney();
            if (money == null)
            {
                WarnOnce("money", "Не удалось прочитать деньги (MoneyRegion) — покупки пропущены.");
                return;
            }
            _c.State.LastKnownMoney = money.Value;
            Log.Info($"Деньги: {money.Value}.");

            foreach (var item in cfg.Upgrades)
            {
                if (!item.Enabled) continue;
                while (_c.State.PurchasedLevel(item.Name) < Math.Max(1, item.Levels))
                {
                    if (item.Cost == null || !MacroPlayer.TryLoad(item.Macro, out var macro))
                    {
                        WarnOnce(item.Name, $"«{item.Name}»: не задана цена или не записан макрос ({item.Macro}) — пропускаю.");
                        goto nextItem;
                    }
                    if (money < item.Cost.Value)
                    {
                        if (cfg.ShopStrictOrder) return; // копим на следующий по приоритету пункт
                        goto nextItem;
                    }

                    Log.Info($"Покупаю «{item.Name}» (уровень {_c.State.PurchasedLevel(item.Name) + 1}) за {item.Cost}.");
                    MacroPlayer.Play(_c.Input, macro);
                    _c.Input.Sleep(600);

                    var after = _c.Hud.ReadMoney();
                    bool ok = after == null || after.Value <= money.Value - item.Cost.Value / 2;
                    if (!ok)
                    {
                        Log.Warn($"Покупка «{item.Name}» не подтвердилась (деньги {money} → {after}). Проверьте макрос.");
                        return;
                    }

                    _c.State.PurchasedLevels[item.Name] = _c.State.PurchasedLevel(item.Name) + 1;
                    money = after ?? money.Value - item.Cost.Value;
                    _c.State.LastKnownMoney = money.Value;
                    if (item.LaneWidthMs != null) _c.State.CurrentLaneWidthMs = item.LaneWidthMs;
                    if (!string.IsNullOrWhiteSpace(item.SelectToolKey))
                    {
                        _c.State.CurrentToolKey = item.SelectToolKey;
                        _c.Input.Tap(item.SelectToolKey!);
                    }
                    _c.SaveState();
                    Log.Good($"Куплено: «{item.Name}». Осталось денег: {money}.");
                }
            nextItem:;
            }
        }

        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) Log.Warn(message);
        }
    }
}
