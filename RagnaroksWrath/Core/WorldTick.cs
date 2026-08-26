using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using RavenIron.RagnaroksWrath.Config;

namespace RavenIron.RagnaroksWrath.Core
{
    /// <summary>
    /// The one place anything in this mod is driven from.
    ///
    /// HOUSE STYLE RULE 2: a time-budgeted cursor driven from a single Update, NOT coroutines.
    /// Every long-lived coroutine in this codebase's lineage independently grew the same bug —
    /// a `while (true)` whose body can `continue` past its only `yield`, hard-locking the game.
    /// It reached production once. A rule you must remember at every future edit is a rule that
    /// will eventually be forgotten, so the shape that cannot express the bug is the one used.
    ///
    /// Systems register once and are round-robined. Each Update spends at most
    /// <see cref="ModConfig.TickBudgetMs"/> milliseconds total across all of them; whatever is
    /// left over resumes next frame from where the cursor stopped. No system can starve another,
    /// because the cursor never resets to zero — it advances.
    /// </summary>
    public class WorldTick : MonoBehaviour
    {
        private static readonly List<IWorldSystem> _systems = new List<IWorldSystem>();
        private static readonly Dictionary<IWorldSystem, float> _lastRun =
            new Dictionary<IWorldSystem, float>();

        private static int _cursor;
        private static bool _initialised;

        private readonly Stopwatch _sw = new Stopwatch();

        /// <summary>
        /// Register a system. Safe to call before ZNet exists; Initialise is deferred until the
        /// first tick where we know whether this process is the simulation authority.
        /// </summary>
        public static void Register(IWorldSystem system)
        {
            if (system == null) return;
            if (_systems.Contains(system)) return;

            _systems.Add(system);
            _lastRun[system] = 0f;
        }

        public static int SystemCount => _systems.Count;

        private void Update()
        {
            // Nothing runs until we know what this process is. IsSimulationAuthority is false
            // while ZNet is null, so this also covers the main-menu case.
            if (!RagnaroksWrath.IsSimulationAuthority()) return;
            if (_systems.Count == 0) return;

            if (!_initialised)
            {
                InitialiseSystems();
                _initialised = true;
            }

            float now = Time.realtimeSinceStartup;
            float budgetMs = ModConfig.TickBudgetMs.Value;

            _sw.Restart();

            // Walk at most one full lap per frame. The cursor persists across frames, so a lap
            // interrupted by the budget resumes rather than restarting — that is what stops the
            // systems early in the list from starving the ones after them.
            int examined = 0;
            while (examined < _systems.Count && _sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                IWorldSystem system = _systems[_cursor];
                _cursor = (_cursor + 1) % _systems.Count;
                examined++;

                if (!system.Enabled) continue;

                float last = _lastRun[system];
                float due = now - last;
                if (due < system.IntervalSeconds) continue;

                _lastRun[system] = now;

                // One misbehaving system must not take the others down with it. This is the
                // gameplay-path sibling of House Style rule 3: catch, log once, keep going.
                try
                {
                    system.Tick(due);
                }
                catch (Exception ex)
                {
                    RagnaroksWrath.Log.LogError($"[{system.Name}] tick threw: {ex}");
                }
            }

            _sw.Stop();

            MaybeAutosave(now);
        }

        private static float _lastSave;

        /// <summary>
        /// Periodic write-behind. Persistence.Save is a no-op when nothing is dirty, so this
        /// costs a comparison on most passes.
        ///
        /// Deliberately time-based rather than change-based: a busy server would otherwise write
        /// on every drift update, and an idle one would never write at all.
        /// </summary>
        private static void MaybeAutosave(float now)
        {
            float interval = ModConfig.AutosaveIntervalSeconds.Value;
            if (interval <= 0f) return;
            if (now - _lastSave < interval) return;

            _lastSave = now;
            Persistence.Save();
        }

        private static void InitialiseSystems()
        {
            // Load stored zone drift before any system reads it. Never throws; a missing or
            // corrupt store leaves an empty, usable state.
            Persistence.Load();

            foreach (IWorldSystem system in _systems)
            {
                try
                {
                    system.Initialise();
                    RagnaroksWrath.Log.LogInfo(
                        $"[{system.Name}] initialised (enabled={system.Enabled}, interval={system.IntervalSeconds}s)");
                }
                catch (Exception ex)
                {
                    RagnaroksWrath.Log.LogError($"[{system.Name}] failed to initialise: {ex}");
                }
            }

            // Proof-of-life line. A silent success and a silent no-op are indistinguishable from
            // outside the game, so this exists specifically so "nothing is happening" can be
            // diagnosed without a round-trip.
            RagnaroksWrath.Log.LogInfo(
                $"WorldTick online — {_systems.Count} system(s), budget {ModConfig.TickBudgetMs.Value}ms/frame, " +
                $"dedicated={RagnaroksWrath.IsDedicated()}");
        }

        private void OnDestroy()
        {
            // Last chance to flush. Leaving the world, shutting down the server, or unloading
            // the plugin all land here — without this, up to one autosave interval of drift is
            // lost every single session, which would look like the mod randomly forgetting things.
            Persistence.Save(force: true);

            // The health ledger has the same write-behind shape (dirty flag, 60s cadence in
            // HealthSystem) and so the same shutdown hole: found live on 2026-08-26, when a
            // clean server stop cost ~0.02 exposure — the last unsaved minute of it. Every
            // store with a write-behind cadence must appear in this method; TitleStore is
            // absent because it saves on Set.
            HealthStore.SaveIfDirty();
            RivalryLedger.SaveIfDirty();

            _systems.Clear();
            _lastRun.Clear();
            _cursor = 0;
            _initialised = false;
            _lastSave = 0f;
        }
    }
}
