#!/usr/bin/env python3
"""Summarize Unity level run JSONL logs for quick calibration/testing review."""

from __future__ import annotations

import argparse
import json
import math
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


DEFAULT_LOG_PATHS = [
    Path.home() / "Library/Application Support/DefaultCompany/EmotionalPlatformer/RunLogs/level_runs.jsonl",
    Path.cwd() / "RunLogs" / "level_runs.jsonl",
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read and summarize EmotionalPlatformer level_runs.jsonl logs."
    )
    parser.add_argument(
        "path",
        nargs="?",
        help="Path to level_runs.jsonl. If omitted, tries the default Unity persistent-data location.",
    )
    return parser.parse_args()


def resolve_log_path(cli_path: str | None) -> Path:
    if cli_path:
        return Path(cli_path).expanduser().resolve()

    for candidate in DEFAULT_LOG_PATHS:
        if candidate.exists():
            return candidate

    checked = "\n".join(f"  - {path}" for path in DEFAULT_LOG_PATHS)
    raise FileNotFoundError(
        "Could not find level_runs.jsonl automatically. Checked:\n" + checked
    )


def load_runs(path: Path) -> list[dict[str, Any]]:
    runs: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line_number, raw in enumerate(handle, start=1):
            line = raw.strip()
            if not line:
                continue
            try:
                runs.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSON on line {line_number}: {exc}") from exc
    return runs


def fmt_num(value: Any, digits: int = 2) -> str:
    if value is None:
        return "-"
    if isinstance(value, bool):
        return str(value)
    if isinstance(value, (int, float)):
        if isinstance(value, float) and (math.isnan(value) or math.isinf(value)):
            return "-"
        return f"{value:.{digits}f}" if isinstance(value, float) else str(value)
    return str(value)


def effective_chunk_name(slot: dict[str, Any]) -> str:
    return slot.get("spawnedChunkName") or slot.get("selectedPrefabName") or "Unknown"


def effective_chunk_difficulty(slot: dict[str, Any]) -> float | None:
    spawned = slot.get("spawnedDifficulty", -1)
    if isinstance(spawned, (int, float)) and spawned >= 0:
        return float(spawned)
    selected = slot.get("selectedDifficulty", -1)
    if isinstance(selected, (int, float)) and selected >= 0:
        return float(selected)
    return None


def selected_candidate_type(slot: dict[str, Any]) -> str:
    value = slot.get("selectedCandidateType")
    if value:
        return str(value)
    return "legacy_or_handcrafted"


def selected_source_name(slot: dict[str, Any]) -> str:
    value = slot.get("selectedSourcePrefabName")
    if value:
        return str(value)
    return slot.get("selectedPrefabName") or "Unknown"


def behaviour_field(run: dict[str, Any], field: str) -> Any:
    adaptation = run.get("adaptation", {}) or {}
    return adaptation.get(field)


def has_behaviour_summary(run: dict[str, Any]) -> bool:
    adaptation = run.get("adaptation", {}) or {}
    keys = [
        "hesitationScore",
        "momentumFluidity",
        "directionReversalRate",
        "avgRetryDelay",
        "deathClusteringRatio",
        "engagementScore",
    ]
    return any(isinstance(adaptation.get(key), (int, float)) for key in keys)


def format_summary_line(run: dict[str, Any]) -> str:
    adaptation = run.get("adaptation", {}) or {}
    target = run.get("targetDifficultyBeforeRun")
    actual = run.get("actualLevelDifficultyScore")
    delta = None
    if isinstance(target, (int, float)) and isinstance(actual, (int, float)):
        delta = actual - target

    return (
        f"{run.get('runId', 'unknown')} | "
        f"seed={run.get('runSeed')} | "
        f"replay={run.get('isReplay', False)} | "
        f"target={fmt_num(target)} | "
        f"actual={fmt_num(actual)} | "
        f"delta={fmt_num(delta)} | "
        f"deaths={run.get('deathsThisLevel', 0)} | "
        f"dpc={fmt_num(run.get('deathsPerChunk'))} | "
        f"tpc={fmt_num(run.get('timePerChunk'))} | "
        f"adapt={adaptation.get('decisionCode', '-') or '-'} "
        f"({fmt_num(adaptation.get('targetBefore'))}->{fmt_num(adaptation.get('targetAfter'))})"
    )


def print_header(title: str) -> None:
    print()
    print(title)
    print("=" * len(title))


def print_latest_run_details(run: dict[str, Any]) -> None:
    adaptation = run.get("adaptation", {}) or {}
    print_header("Latest Run Detail")
    print(f"Run ID: {run.get('runId', '-')}")
    print(f"Generated At: {run.get('generatedAtUtc', '-')}")
    print(f"Seed: {run.get('runSeed', '-')}")
    print(f"Replay: {run.get('isReplay', False)}")
    print(
        f"Target: {fmt_num(run.get('targetDifficultyBeforeRun'))} | "
        f"Actual: {fmt_num(run.get('actualLevelDifficultyScore'))} | "
        f"Avg Chunk: {fmt_num(run.get('avgChunkDifficulty'))}"
    )
    print(
        f"Deaths: {run.get('deathsThisLevel', 0)} | "
        f"Deaths/Chunk: {fmt_num(run.get('deathsPerChunk'))} | "
        f"Time/Chunk: {fmt_num(run.get('timePerChunk'))} | "
        f"Time: {fmt_num(run.get('levelTimeSeconds'))}s"
    )
    print(
        f"Chunks: {run.get('chunkCountThisLevel', 0)} | "
        f"Hazards: {run.get('hazardChunkCount', 0)} | "
        f"Estimated Jumps: {run.get('totalEstimatedJumps', 0)} | "
        f"Vertical: {run.get('verticalChunkCount', 0)}"
    )
    print(
        f"Transition Pressure: count={run.get('transitionPressureCount', 0)} | "
        f"high={run.get('highPressureTransitionCount', 0)} | "
        f"score={fmt_num(run.get('transitionPressureScore'))}"
    )
    print(
        f"Adaptation: {adaptation.get('decisionCode', '-') or '-'} | "
        f"{adaptation.get('decisionText', '-') or '-'} | "
        f"{fmt_num(adaptation.get('targetBefore'))} -> {fmt_num(adaptation.get('targetAfter'))}"
    )
    if adaptation.get("controllerName") or adaptation.get("evidenceSummary"):
        print(
            f"Controller: {adaptation.get('controllerName', '-') or '-'} | "
            f"{adaptation.get('evidenceSummary', '-') or '-'}"
        )
    if has_behaviour_summary(run):
        print(
            "Behaviour: "
            f"engagement={fmt_num(adaptation.get('engagementScore'))} | "
            f"hesitation={fmt_num(adaptation.get('hesitationScore'))} | "
            f"momentum={fmt_num(adaptation.get('momentumFluidity'))} | "
            f"reversals/s={fmt_num(adaptation.get('directionReversalRate'))} | "
            f"retryDelay={fmt_num(adaptation.get('avgRetryDelay'))} | "
            f"deathCluster={fmt_num(adaptation.get('deathClusteringRatio'))} | "
            f"chunks={adaptation.get('behaviourChunksTraversed', '-')} | "
            f"frames={adaptation.get('behaviourTraversalFrames', '-')}"
        )
    if "markovLearningApplied" in adaptation:
        print(
            "Markov Learning: "
            f"applied={adaptation.get('markovLearningApplied')} | "
            f"quality={fmt_num(adaptation.get('markovLearningQuality'))} | "
            f"capped={adaptation.get('markovPositiveReinforcementCapped')} | "
            f"delivered-target={fmt_num(adaptation.get('markovDeliveredTargetDelta'))} | "
            f"transitions={adaptation.get('markovTransitionsUpdated', '-')}"
        )


def slot_rows_for_table(slots: Iterable[dict[str, Any]]) -> list[list[str]]:
    rows: list[list[str]] = []
    for slot in slots:
        slot_target = slot.get("slotTargetDifficulty") if slot.get("hasSlotTargetDifficulty") else None
        eff_diff = effective_chunk_difficulty(slot)
        delta = None
        if slot_target is not None and eff_diff is not None:
            delta = eff_diff - float(slot_target)
        rows.append(
            [
                str(slot.get("sequenceIndex", "-")),
                str(slot.get("generatedSlotIndex", "-")),
                fmt_num(slot_target),
                slot.get("selectedPrefabName", "-") or "-",
                selected_candidate_type(slot),
                selected_source_name(slot),
                fmt_num(slot.get("selectedDifficulty"), 0),
                slot.get("spawnedChunkName", "-") or "-",
                fmt_num(slot.get("spawnedDifficulty"), 0),
                fmt_num(delta),
                slot.get("replacementMode", "-") or "-",
                slot.get("replacementReason", "-") or "-",
                slot.get("generatedRejectionReason", "-") or "-",
                slot.get("transitionPressureSeverity", "-") or "-",
                slot.get("transitionPressureReason", "-") or "-",
                str(slot.get("deathsAttributedToSlot", 0)),
            ]
        )
    return rows


def print_table(headers: list[str], rows: list[list[str]]) -> None:
    widths = [len(header) for header in headers]
    for row in rows:
        for idx, cell in enumerate(row):
            widths[idx] = max(widths[idx], len(cell))

    def format_row(row: list[str]) -> str:
        return " | ".join(cell.ljust(widths[idx]) for idx, cell in enumerate(row))

    print(format_row(headers))
    print("-+-".join("-" * width for width in widths))
    for row in rows:
        print(format_row(row))


def print_latest_slot_table(run: dict[str, Any]) -> None:
    print_header("Latest Run Slot Table")
    slots = run.get("slots", []) or []
    if not slots:
        print("No slot records.")
        return

    headers = [
        "seq",
        "gen",
        "target",
        "selected",
        "candType",
        "source",
        "selDiff",
        "spawned",
        "spnDiff",
        "delta",
        "replace",
        "reason",
        "accept/reject",
        "pressure",
        "pressureReason",
        "deaths",
    ]
    print_table(headers, slot_rows_for_table(slots))


def selected_vs_spawned_mismatches(slots: Iterable[dict[str, Any]]) -> list[str]:
    messages: list[str] = []
    for slot in slots:
        selected_name = slot.get("selectedPrefabName")
        spawned_name = slot.get("spawnedChunkName")
        selected_diff = slot.get("selectedDifficulty")
        spawned_diff = slot.get("spawnedDifficulty")
        if not selected_name or not spawned_name:
            continue

        generated_candidate = selected_candidate_type(slot) == "generated_blueprint"
        generated_name_match = generated_candidate and str(spawned_name).startswith(str(selected_name))
        name_changed = selected_name != spawned_name and not generated_name_match
        diff_changed = (
            isinstance(selected_diff, (int, float))
            and isinstance(spawned_diff, (int, float))
            and selected_diff >= 0
            and spawned_diff >= 0
            and float(selected_diff) != float(spawned_diff)
        )

        if name_changed or diff_changed:
            messages.append(
                f"slot {slot.get('sequenceIndex')}: {selected_name} ({fmt_num(selected_diff, 0)}) -> "
                f"{spawned_name} ({fmt_num(spawned_diff, 0)}) "
                f"[{slot.get('replacementMode', '-')}; {slot.get('replacementReason', '-') or '-'}; "
                f"{slot.get('generatedRejectionReason', '-') or '-'}]"
            )
    return messages


def late_slot_delta_messages(slots: Iterable[dict[str, Any]]) -> list[str]:
    generated_slots = [slot for slot in slots if slot.get("hasSlotTargetDifficulty")]
    if not generated_slots:
        return []

    generated_slots = sorted(generated_slots, key=lambda slot: slot.get("generatedSlotIndex", -1))
    late_slots = generated_slots[-3:]
    messages: list[str] = []

    for slot in late_slots:
        target = slot.get("slotTargetDifficulty")
        eff_diff = effective_chunk_difficulty(slot)
        if not isinstance(target, (int, float)) or eff_diff is None:
            continue
        delta = eff_diff - float(target)
        if delta <= -1.0:
            messages.append(
                f"late-slot undershoot: slot {slot.get('sequenceIndex')} target {fmt_num(target)} "
                f"but effective difficulty {fmt_num(eff_diff)} ({effective_chunk_name(slot)})"
            )
        elif delta >= 1.0:
            messages.append(
                f"late-slot overshoot: slot {slot.get('sequenceIndex')} target {fmt_num(target)} "
                f"but effective difficulty {fmt_num(eff_diff)} ({effective_chunk_name(slot)})"
            )

    avg_delta_values = []
    for slot in late_slots:
        target = slot.get("slotTargetDifficulty")
        eff_diff = effective_chunk_difficulty(slot)
        if isinstance(target, (int, float)) and eff_diff is not None:
            avg_delta_values.append(eff_diff - float(target))

    if avg_delta_values:
        avg_delta = sum(avg_delta_values) / len(avg_delta_values)
        if avg_delta <= -0.75:
            messages.append(f"late-slot average undershoot: {avg_delta:.2f}")
        elif avg_delta >= 0.75:
            messages.append(f"late-slot average overshoot: {avg_delta:.2f}")

    return messages


def death_concentration_messages(run: dict[str, Any]) -> list[str]:
    messages: list[str] = []
    total_deaths = int(run.get("deathsThisLevel", 0) or 0)
    slots = run.get("slots", []) or []
    if total_deaths <= 0 or not slots:
        return messages

    by_slot = sorted(
        ((slot.get("sequenceIndex", -1), int(slot.get("deathsAttributedToSlot", 0) or 0), effective_chunk_name(slot)) for slot in slots),
        key=lambda item: item[1],
        reverse=True,
    )
    top_slot_index, top_slot_deaths, top_slot_name = by_slot[0]
    if top_slot_deaths > 0:
        share = top_slot_deaths / max(1, total_deaths)
        if share >= 0.5:
            messages.append(
                f"death concentration: slot {top_slot_index} ({top_slot_name}) accounts for "
                f"{top_slot_deaths}/{total_deaths} deaths"
            )

    death_events = run.get("deathEvents", []) or []
    chunk_counter = Counter(event.get("chunkName", "Unknown") for event in death_events)
    if chunk_counter:
        chunk_name, count = chunk_counter.most_common(1)[0]
        if count / max(1, total_deaths) >= 0.5:
            messages.append(
                f"death concentration by chunk: {chunk_name} accounts for {count}/{total_deaths} deaths"
            )

    return messages


def print_latest_warnings(run: dict[str, Any]) -> None:
    print_header("Latest Run Warnings / Flags")
    warnings = []
    warnings.extend(late_slot_delta_messages(run.get("slots", []) or []))
    warnings.extend(death_concentration_messages(run))
    warnings.extend(transition_pressure_messages(run.get("slots", []) or []))

    mismatches = selected_vs_spawned_mismatches(run.get("slots", []) or [])
    if mismatches:
        warnings.append(f"selected vs spawned mismatches: {len(mismatches)}")
        warnings.extend(f"  - {message}" for message in mismatches[:5])

    if not warnings:
        print("No major flags from the current heuristic checks.")
        return

    for message in warnings:
        print(f"- {message}")


def transition_pressure_messages(slots: Iterable[dict[str, Any]]) -> list[str]:
    messages: list[str] = []
    for slot in slots:
        if not slot.get("transitionPressurePenalized"):
            continue

        previous = slot.get("previousSpawnedChunkName", "-") or "-"
        current = effective_chunk_name(slot)
        severity = slot.get("transitionPressureSeverity", "unknown") or "unknown"
        reason = slot.get("transitionPressureReason", "unknown") or "unknown"
        multiplier = slot.get("transitionPressureMultiplier")

        messages.append(
            f"transition pressure: {previous} -> {current} "
            f"[{severity}; {reason}; multiplier={fmt_num(multiplier)}]"
        )

    return messages


def run_target_delta(run: dict[str, Any]) -> float | None:
    target = run.get("targetDifficultyBeforeRun")
    actual = run.get("actualLevelDifficultyScore")
    if isinstance(target, (int, float)) and isinstance(actual, (int, float)):
        return float(actual) - float(target)
    return None


def iter_slot_target_deltas(runs: Iterable[dict[str, Any]]) -> Iterable[float]:
    for run in runs:
        for slot in run.get("slots", []) or []:
            if not slot.get("hasSlotTargetDifficulty"):
                continue
            target = slot.get("slotTargetDifficulty")
            eff_diff = effective_chunk_difficulty(slot)
            if isinstance(target, (int, float)) and eff_diff is not None:
                yield eff_diff - float(target)


def progression_delta_for_run(run: dict[str, Any]) -> float | None:
    slots = [
        slot
        for slot in run.get("slots", []) or []
        if slot.get("hasSlotTargetDifficulty") and effective_chunk_difficulty(slot) is not None
    ]
    if len(slots) < 3:
        return None

    slots = sorted(slots, key=lambda slot: slot.get("generatedSlotIndex", -1))
    region_size = max(1, len(slots) // 3)
    first = [effective_chunk_difficulty(slot) for slot in slots[:region_size]]
    last = [effective_chunk_difficulty(slot) for slot in slots[-region_size:]]

    first_values = [value for value in first if value is not None]
    last_values = [value for value in last if value is not None]
    if not first_values or not last_values:
        return None

    return (sum(last_values) / len(last_values)) - (sum(first_values) / len(first_values))


def replacement_difficulty_deltas(runs: Iterable[dict[str, Any]]) -> list[float]:
    deltas: list[float] = []
    for run in runs:
        for slot in run.get("slots", []) or []:
            if not slot.get("replacementAttempted"):
                continue
            selected = slot.get("selectedDifficulty")
            spawned = slot.get("spawnedDifficulty")
            if (
                isinstance(selected, (int, float))
                and isinstance(spawned, (int, float))
                and selected >= 0
                and spawned >= 0
            ):
                deltas.append(float(spawned) - float(selected))
    return deltas


def adaptation_audit_messages(runs: Iterable[dict[str, Any]]) -> list[str]:
    messages: list[str] = []
    for run in runs:
        adaptation = run.get("adaptation", {}) or {}
        before = adaptation.get("targetBefore")
        after = adaptation.get("targetAfter")
        target_delta = run_target_delta(run)
        run_id = run.get("runId", "unknown")

        if not isinstance(before, (int, float)) or not isinstance(after, (int, float)):
            continue
        if target_delta is None:
            continue

        target_change = float(after) - float(before)
        clean_run = bool(adaptation.get("cleanRun"))

        if target_change > 0 and target_delta > 0.75:
            messages.append(
                f"{run_id}: increased target despite delivered difficulty overshooting target by {target_delta:.2f}"
            )
        elif target_change == 0 and clean_run and target_delta > 1.0:
            messages.append(
                f"{run_id}: clean run kept target while delivered difficulty overshot by {target_delta:.2f}"
            )
        elif target_change < 0 and target_delta < -0.75:
            messages.append(
                f"{run_id}: decreased target even though delivered difficulty was below target by {abs(target_delta):.2f}"
            )

    return messages


def print_calibration_evaluation(runs: list[dict[str, Any]]) -> None:
    print_header("Calibration Evaluation")

    target_deltas = [delta for delta in (run_target_delta(run) for run in runs) if delta is not None]
    slot_deltas = list(iter_slot_target_deltas(runs))
    progression_deltas = [
        delta for delta in (progression_delta_for_run(run) for run in runs) if delta is not None
    ]
    replacement_deltas = replacement_difficulty_deltas(runs)

    if target_deltas:
        avg_delta = sum(target_deltas) / len(target_deltas)
        avg_abs_delta = sum(abs(delta) for delta in target_deltas) / len(target_deltas)
        overshoots = sum(1 for delta in target_deltas if delta > 1.0)
        undershoots = sum(1 for delta in target_deltas if delta < -1.0)
        print(
            f"Target tracking: runs={len(target_deltas)} | "
            f"avg actual-target={avg_delta:.2f} | avg abs error={avg_abs_delta:.2f} | "
            f"overshoot>1={overshoots} | undershoot>1={undershoots}"
        )
    else:
        print("Target tracking: no target/actual difficulty pairs available.")

    if slot_deltas:
        avg_slot_delta = sum(slot_deltas) / len(slot_deltas)
        avg_abs_slot_delta = sum(abs(delta) for delta in slot_deltas) / len(slot_deltas)
        large_errors = sum(1 for delta in slot_deltas if abs(delta) > 1.0)
        print(
            f"Slot target tracking: slots={len(slot_deltas)} | "
            f"avg selected-target={avg_slot_delta:.2f} | avg abs error={avg_abs_slot_delta:.2f} | "
            f"abs error>1={large_errors}"
        )
    else:
        print("Slot target tracking: no slot target records available.")

    if progression_deltas:
        avg_progression = sum(progression_deltas) / len(progression_deltas)
        improving_runs = sum(1 for delta in progression_deltas if delta > 0)
        print(
            f"Ramp quality: avg last-third minus first-third={avg_progression:.2f} | "
            f"runs ramping upward={improving_runs}/{len(progression_deltas)}"
        )
    else:
        print("Ramp quality: not enough slot data to evaluate.")

    attempted = sum(
        1
        for run in runs
        for slot in run.get("slots", []) or []
        if slot.get("replacementAttempted")
    )
    succeeded = sum(
        1
        for run in runs
        for slot in run.get("slots", []) or []
        if slot.get("replacementSucceeded")
    )
    if attempted > 0:
        avg_replacement_delta = (
            sum(replacement_deltas) / len(replacement_deltas) if replacement_deltas else None
        )
        avg_abs_replacement_delta = (
            sum(abs(delta) for delta in replacement_deltas) / len(replacement_deltas)
            if replacement_deltas
            else None
        )
        print(
            f"Replacement stability: attempted={attempted} | succeeded={succeeded} | "
            f"avg diff shift={fmt_num(avg_replacement_delta)} | "
            f"avg abs diff shift={fmt_num(avg_abs_replacement_delta)}"
        )
    else:
        print("Replacement stability: no generated replacement attempts in these logs.")

    audit_messages = adaptation_audit_messages(runs)
    print("Adaptation audit:")
    if not audit_messages:
        print("  - no target-change calibration flags found")
    else:
        for message in audit_messages[:8]:
            print(f"  - {message}")
        if len(audit_messages) > 8:
            print(f"  - ... {len(audit_messages) - 8} more")


def print_aggregate_summary(runs: list[dict[str, Any]]) -> None:
    print_header("Aggregate Summary")
    run_count = len(runs)
    avg_target = average(run.get("targetDifficultyBeforeRun") for run in runs)
    avg_actual = average(run.get("actualLevelDifficultyScore") for run in runs)
    avg_deaths = average(run.get("deathsThisLevel") for run in runs)
    avg_deaths_per_chunk = average(run.get("deathsPerChunk") for run in runs)
    avg_time_per_chunk = average(run.get("timePerChunk") for run in runs)
    avg_transition_pressure = average(run.get("transitionPressureScore") for run in runs)
    avg_engagement = average(behaviour_field(run, "engagementScore") for run in runs)
    avg_hesitation = average(behaviour_field(run, "hesitationScore") for run in runs)
    avg_momentum = average(behaviour_field(run, "momentumFluidity") for run in runs)
    avg_reversal_rate = average(behaviour_field(run, "directionReversalRate") for run in runs)
    avg_retry_delay = average(
        value
        for run in runs
        for value in [behaviour_field(run, "avgRetryDelay")]
        if isinstance(value, (int, float)) and value >= 0
    )
    avg_death_cluster = average(behaviour_field(run, "deathClusteringRatio") for run in runs)
    avg_markov_quality = average(behaviour_field(run, "markovLearningQuality") for run in runs)
    markov_applied_count = sum(1 for run in runs if behaviour_field(run, "markovLearningApplied") is True)
    markov_capped_count = sum(1 for run in runs if behaviour_field(run, "markovPositiveReinforcementCapped") is True)
    markov_transition_updates = sum(
        int(behaviour_field(run, "markovTransitionsUpdated") or 0)
        for run in runs
    )
    total_pressure_count = sum(int(run.get("transitionPressureCount", 0) or 0) for run in runs)
    total_high_pressure_count = sum(int(run.get("highPressureTransitionCount", 0) or 0) for run in runs)

    print(
        f"Runs: {run_count} | "
        f"Avg target: {fmt_num(avg_target)} | "
        f"Avg actual: {fmt_num(avg_actual)} | "
        f"Avg deaths: {fmt_num(avg_deaths)} | "
        f"Avg dpc: {fmt_num(avg_deaths_per_chunk)} | "
        f"Avg tpc: {fmt_num(avg_time_per_chunk)}"
    )
    print(
        f"Transition pressure: total={total_pressure_count} | "
        f"high={total_high_pressure_count} | "
        f"avg score/run={fmt_num(avg_transition_pressure)}"
    )
    print(
        f"Behavioural signals: engagement={fmt_num(avg_engagement)} | "
        f"hesitation={fmt_num(avg_hesitation)} | momentum={fmt_num(avg_momentum)} | "
        f"reversals/s={fmt_num(avg_reversal_rate)} | retryDelay={fmt_num(avg_retry_delay)} | "
        f"deathCluster={fmt_num(avg_death_cluster)}"
    )
    print(
        f"Markov learning: applied={markov_applied_count}/{run_count} | "
        f"positive caps={markov_capped_count} | avg quality={fmt_num(avg_markov_quality)} | "
        f"transition updates={markov_transition_updates}"
    )

    selected_counter: Counter[str] = Counter()
    selected_candidate_type_counter: Counter[str] = Counter()
    selected_source_counter: Counter[str] = Counter()
    replaced_counter: Counter[str] = Counter()
    replacement_reason_counter: Counter[str] = Counter()
    rejection_reason_counter: Counter[str] = Counter()
    generated_blueprint_counter: Counter[str] = Counter()
    generated_rows_counter: Counter[str] = Counter()
    death_slot_counter: Counter[str] = Counter()
    death_chunk_counter: Counter[str] = Counter()
    adaptation_counter: Counter[str] = Counter()
    transition_reason_counter: Counter[str] = Counter()
    transition_pair_counter: Counter[str] = Counter()

    for run in runs:
        adaptation = run.get("adaptation", {}) or {}
        adaptation_counter[adaptation.get("decisionCode", "unknown") or "unknown"] += 1
        for slot in run.get("slots", []) or []:
            selected_counter[slot.get("selectedPrefabName", "Unknown") or "Unknown"] += 1
            selected_candidate_type_counter[selected_candidate_type(slot)] += 1
            selected_source_counter[selected_source_name(slot)] += 1
            if slot.get("replacementMode") and slot.get("replacementMode") != "none":
                replaced_counter[slot.get("selectedPrefabName", "Unknown") or "Unknown"] += 1
                replacement_reason_counter[slot.get("replacementReason", "unknown") or "unknown"] += 1
                rejection_reason_counter[slot.get("generatedRejectionReason", "unknown") or "unknown"] += 1
                generated_blueprint_counter[slot.get("generatedBlueprintName", "unknown") or "unknown"] += 1
                generated_rows_counter[slot.get("generatedBlueprintRows", "unknown") or "unknown"] += 1
            slot_deaths = int(slot.get("deathsAttributedToSlot", 0) or 0)
            if slot_deaths > 0:
                death_slot_counter[f"slot {slot.get('sequenceIndex')}: {effective_chunk_name(slot)}"] += slot_deaths
            if slot.get("transitionPressurePenalized"):
                reason = slot.get("transitionPressureReason", "unknown") or "unknown"
                previous = slot.get("previousSpawnedChunkName", "Unknown") or "Unknown"
                current = effective_chunk_name(slot)
                transition_reason_counter[reason] += 1
                transition_pair_counter[f"{previous} -> {current}"] += 1

        for event in run.get("deathEvents", []) or []:
            death_chunk_counter[event.get("chunkName", "Unknown") or "Unknown"] += 1

    print_top_counter("Most selected chunks", selected_counter, 5)
    print_top_counter("Selected candidate types", selected_candidate_type_counter, 5)
    print_top_counter("Most selected source families", selected_source_counter, 5)
    print_top_counter("Most replaced selected chunks", replaced_counter, 5)
    print_top_counter("Replacement reasons", replacement_reason_counter, 5)
    print_top_counter("Generated acceptance/rejection reasons", rejection_reason_counter, 5)
    print_top_counter("Generated blueprint names", generated_blueprint_counter, 5)
    print_top_counter("Generated blueprint layouts", generated_rows_counter, 5)
    print_top_counter("Death-heavy slots", death_slot_counter, 5)
    print_top_counter("Death-heavy chunks", death_chunk_counter, 5)
    print_top_counter("Transition pressure reasons", transition_reason_counter, 5)
    print_top_counter("Transition pressure pairs", transition_pair_counter, 5)
    print_top_counter("Adaptation decisions", adaptation_counter, 5)


def print_top_counter(title: str, counter: Counter[str], limit: int) -> None:
    print()
    print(title + ":")
    if not counter:
        print("  - none")
        return
    for name, count in counter.most_common(limit):
        print(f"  - {name}: {count}")


def average(values: Iterable[Any]) -> float | None:
    nums = [float(value) for value in values if isinstance(value, (int, float))]
    if not nums:
        return None
    return sum(nums) / len(nums)


def print_run_summaries(runs: list[dict[str, Any]]) -> None:
    print_header("Run Summaries")
    for run in runs:
        print(format_summary_line(run))


def main() -> int:
    args = parse_args()
    try:
        path = resolve_log_path(args.path)
        runs = load_runs(path)
    except (FileNotFoundError, ValueError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1

    if not runs:
        print(f"No runs found in {path}", file=sys.stderr)
        return 1

    latest = runs[-1]

    print(f"Log file: {path}")
    print_run_summaries(runs)
    print_latest_run_details(latest)
    print_latest_slot_table(latest)
    print_latest_warnings(latest)
    print_calibration_evaluation(runs)
    print_aggregate_summary(runs)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
