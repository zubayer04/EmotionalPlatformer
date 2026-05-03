#!/usr/bin/env python3
"""Create dissertation-friendly evaluation outputs from EmotionalPlatformer JSONL logs."""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from collections import Counter
from datetime import datetime
from pathlib import Path
from typing import Any, Iterable


DEFAULT_LOG_PATHS = [
    Path.home() / "Library/Application Support/DefaultCompany/EmotionalPlatformer/RunLogs/level_runs.jsonl",
    Path.cwd() / "RunLogs" / "level_runs.jsonl",
]

DEFAULT_MARKOV_WEIGHTS_PATH = (
    Path.home()
    / "Library/Application Support/DefaultCompany/EmotionalPlatformer/MarkovWeights/learned_weights.json"
)

DEFAULT_MARKOV_SOURCE_PATH = Path.cwd() / "Assets/Scripts/MarkovWeightTable.cs"

CHUNK_TAGS = ["Safe", "Gap", "Spikes", "Vertical", "Precision", "Rest"]
DIFFICULTY_BANDS = ["Low", "Medium", "High"]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Generate Markdown/CSV evaluation summaries for EmotionalPlatformer run logs. "
            "Use --dataset Label=path for baseline/speculative comparisons."
        )
    )
    parser.add_argument(
        "path",
        nargs="?",
        help="Path to a JSONL file. Ignored if --dataset is provided.",
    )
    parser.add_argument(
        "--dataset",
        action="append",
        default=[],
        metavar="LABEL=PATH",
        help="Named dataset for comparison. Can be repeated.",
    )
    parser.add_argument(
        "--latest",
        type=int,
        default=0,
        help="Use only the latest N runs from each dataset.",
    )
    parser.add_argument(
        "--out-dir",
        default=None,
        help="Directory for report outputs. Defaults to EvaluationReports/evaluation_<timestamp>.",
    )
    parser.add_argument(
        "--markov-weights",
        default=str(DEFAULT_MARKOV_WEIGHTS_PATH),
        help="Path to learned_weights.json for Markov drift audit.",
    )
    parser.add_argument(
        "--markov-source",
        default=str(DEFAULT_MARKOV_SOURCE_PATH),
        help="Path to MarkovWeightTable.cs for baseline parsing.",
    )
    return parser.parse_args()


def resolve_default_log_path() -> Path:
    for candidate in DEFAULT_LOG_PATHS:
        if candidate.exists():
            return candidate

    checked = "\n".join(f"  - {path}" for path in DEFAULT_LOG_PATHS)
    raise FileNotFoundError("Could not find level_runs.jsonl automatically. Checked:\n" + checked)


def parse_datasets(args: argparse.Namespace) -> list[tuple[str, Path]]:
    if args.dataset:
        datasets: list[tuple[str, Path]] = []
        for raw in args.dataset:
            if "=" not in raw:
                raise ValueError(f"Invalid dataset '{raw}'. Expected LABEL=PATH.")
            label, path = raw.split("=", 1)
            label = label.strip()
            if not label:
                raise ValueError(f"Invalid dataset '{raw}'. Label cannot be empty.")
            datasets.append((label, Path(path).expanduser().resolve()))
        return datasets

    path = Path(args.path).expanduser().resolve() if args.path else resolve_default_log_path()
    return [("current", path)]


def load_runs(path: Path, latest: int = 0) -> list[dict[str, Any]]:
    runs: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line_number, raw in enumerate(handle, start=1):
            line = raw.strip()
            if not line:
                continue
            try:
                runs.append(json.loads(line))
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSON on line {line_number} in {path}: {exc}") from exc

    if latest > 0:
        runs = runs[-latest:]
    return runs


def metric(run: dict[str, Any], key: str, default: Any = None) -> Any:
    return run.get(key, default)


def adaptation(run: dict[str, Any]) -> dict[str, Any]:
    return run.get("adaptation", {}) or {}


def adaptation_metric(run: dict[str, Any], key: str, default: Any = None) -> Any:
    return adaptation(run).get(key, default)


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


def run_delta(run: dict[str, Any]) -> float | None:
    target = metric(run, "targetDifficultyBeforeRun")
    actual = metric(run, "actualLevelDifficultyScore")
    if isinstance(target, (int, float)) and isinstance(actual, (int, float)):
        return float(actual) - float(target)
    return None


def slot_deltas(run: dict[str, Any]) -> list[float]:
    deltas: list[float] = []
    for slot in run.get("slots", []) or []:
        if not slot.get("hasSlotTargetDifficulty"):
            continue
        target = slot.get("slotTargetDifficulty")
        difficulty = effective_chunk_difficulty(slot)
        if isinstance(target, (int, float)) and difficulty is not None:
            deltas.append(difficulty - float(target))
    return deltas


def generated_slot_count(run: dict[str, Any]) -> int:
    return sum(
        1
        for slot in run.get("slots", []) or []
        if slot.get("selectedCandidateType") == "generated_blueprint"
        or slot.get("replacementSucceeded")
    )


def avg(values: Iterable[Any]) -> float | None:
    nums = [
        float(value)
        for value in values
        if isinstance(value, (int, float)) and not math.isnan(float(value))
    ]
    if not nums:
        return None
    return sum(nums) / len(nums)


def fmt(value: Any, digits: int = 2) -> str:
    if value is None:
        return "-"
    if isinstance(value, bool):
        return "yes" if value else "no"
    if isinstance(value, (int, float)):
        if isinstance(value, float) and (math.isnan(value) or math.isinf(value)):
            return "-"
        return f"{float(value):.{digits}f}"
    return str(value)


def summarize_dataset(label: str, path: Path, runs: list[dict[str, Any]]) -> dict[str, Any]:
    deltas = [value for value in (run_delta(run) for run in runs) if value is not None]
    all_slot_deltas = [delta for run in runs for delta in slot_deltas(run)]

    return {
        "label": label,
        "path": str(path),
        "runs": len(runs),
        "generation_modes": ", ".join(
            f"{mode}={count}"
            for mode, count in sorted(Counter(run.get("generationMode", "legacy") or "legacy" for run in runs).items())
        ),
        "avg_target": avg(metric(run, "targetDifficultyBeforeRun") for run in runs),
        "avg_actual": avg(metric(run, "actualLevelDifficultyScore") for run in runs),
        "avg_delta": avg(deltas),
        "avg_abs_delta": avg(abs(delta) for delta in deltas),
        "overshoot_gt_1": sum(1 for delta in deltas if delta > 1.0),
        "undershoot_lt_minus_1": sum(1 for delta in deltas if delta < -1.0),
        "avg_deaths": avg(metric(run, "deathsThisLevel") for run in runs),
        "avg_deaths_per_chunk": avg(metric(run, "deathsPerChunk") for run in runs),
        "avg_time_per_chunk": avg(metric(run, "timePerChunk") for run in runs),
        "avg_pressure_count": avg(metric(run, "transitionPressureCount") for run in runs),
        "high_pressure_total": sum(int(metric(run, "highPressureTransitionCount", 0) or 0) for run in runs),
        "avg_generated_slots": avg(generated_slot_count(run) for run in runs),
        "avg_engagement": avg(adaptation_metric(run, "engagementScore") for run in runs),
        "avg_hesitation": avg(adaptation_metric(run, "hesitationScore") for run in runs),
        "avg_momentum": avg(adaptation_metric(run, "momentumFluidity") for run in runs),
        "avg_reversals": avg(adaptation_metric(run, "directionReversalRate") for run in runs),
        "avg_markov_quality": avg(adaptation_metric(run, "markovLearningQuality") for run in runs),
        "markov_caps": sum(
            1 for run in runs if adaptation_metric(run, "markovPositiveReinforcementCapped") is True
        ),
        "markov_updates": sum(int(adaptation_metric(run, "markovTransitionsUpdated", 0) or 0) for run in runs),
        "slot_avg_abs_delta": avg(abs(delta) for delta in all_slot_deltas),
        "slot_abs_delta_gt_1": sum(1 for delta in all_slot_deltas if abs(delta) > 1.0),
    }


def write_runs_csv(path: Path, datasets: list[tuple[str, list[dict[str, Any]]]]) -> None:
    fields = [
        "dataset",
        "index",
        "runId",
        "seed",
        "generation_mode",
        "target",
        "actual",
        "actual_minus_target",
        "deaths",
        "deaths_per_chunk",
        "time_per_chunk",
        "transition_pressure_count",
        "high_pressure_count",
        "generated_slots",
        "decision",
        "target_before",
        "target_after",
        "clean_run",
        "comfort_run",
        "low_signal_death_run",
        "performance_strain",
        "smoothed_strain",
        "engagement",
        "hesitation",
        "momentum",
        "reversals_per_second",
        "retry_delay",
        "death_clustering",
        "behaviour_chunks",
        "markov_learning_applied",
        "markov_learning_quality",
        "markov_positive_cap",
        "markov_delivered_delta",
        "markov_transitions_updated",
    ]

    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for label, runs in datasets:
            for index, run in enumerate(runs, start=1):
                ad = adaptation(run)
                writer.writerow(
                    {
                        "dataset": label,
                        "index": index,
                        "runId": metric(run, "runId"),
                        "seed": metric(run, "runSeed"),
                        "generation_mode": metric(run, "generationMode", "legacy"),
                        "target": metric(run, "targetDifficultyBeforeRun"),
                        "actual": metric(run, "actualLevelDifficultyScore"),
                        "actual_minus_target": run_delta(run),
                        "deaths": metric(run, "deathsThisLevel"),
                        "deaths_per_chunk": metric(run, "deathsPerChunk"),
                        "time_per_chunk": metric(run, "timePerChunk"),
                        "transition_pressure_count": metric(run, "transitionPressureCount"),
                        "high_pressure_count": metric(run, "highPressureTransitionCount"),
                        "generated_slots": generated_slot_count(run),
                        "decision": ad.get("decisionCode"),
                        "target_before": ad.get("targetBefore"),
                        "target_after": ad.get("targetAfter"),
                        "clean_run": ad.get("cleanRun"),
                        "comfort_run": ad.get("comfortRun"),
                        "low_signal_death_run": ad.get("lowSignalDeathRun"),
                        "performance_strain": ad.get("performanceStrain"),
                        "smoothed_strain": ad.get("smoothedStrain"),
                        "engagement": ad.get("engagementScore"),
                        "hesitation": ad.get("hesitationScore"),
                        "momentum": ad.get("momentumFluidity"),
                        "reversals_per_second": ad.get("directionReversalRate"),
                        "retry_delay": ad.get("avgRetryDelay"),
                        "death_clustering": ad.get("deathClusteringRatio"),
                        "behaviour_chunks": ad.get("behaviourChunksTraversed"),
                        "markov_learning_applied": ad.get("markovLearningApplied"),
                        "markov_learning_quality": ad.get("markovLearningQuality"),
                        "markov_positive_cap": ad.get("markovPositiveReinforcementCapped"),
                        "markov_delivered_delta": ad.get("markovDeliveredTargetDelta"),
                        "markov_transitions_updated": ad.get("markovTransitionsUpdated"),
                    }
                )


def write_slots_csv(path: Path, datasets: list[tuple[str, list[dict[str, Any]]]]) -> None:
    fields = [
        "dataset",
        "run_index",
        "runId",
        "generationMode",
        "sequenceIndex",
        "generatedSlotIndex",
        "slotTargetDifficulty",
        "chunk",
        "source",
        "candidateType",
        "primaryTag",
        "difficulty",
        "slot_delta",
        "replacementMode",
        "generatedBlueprintName",
        "transitionPressureSeverity",
        "transitionPressureReason",
        "transitionPressureScore",
        "deathsAttributedToSlot",
    ]

    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for label, runs in datasets:
            for run_index, run in enumerate(runs, start=1):
                for slot in run.get("slots", []) or []:
                    target = slot.get("slotTargetDifficulty") if slot.get("hasSlotTargetDifficulty") else None
                    difficulty = effective_chunk_difficulty(slot)
                    writer.writerow(
                        {
                            "dataset": label,
                            "run_index": run_index,
                            "runId": metric(run, "runId"),
                            "generationMode": metric(run, "generationMode", "legacy"),
                            "sequenceIndex": slot.get("sequenceIndex"),
                            "generatedSlotIndex": slot.get("generatedSlotIndex"),
                            "slotTargetDifficulty": target,
                            "chunk": effective_chunk_name(slot),
                            "source": slot.get("selectedSourcePrefabName") or slot.get("selectedPrefabName"),
                            "candidateType": slot.get("selectedCandidateType"),
                            "primaryTag": slot.get("spawnedPrimaryTag") or slot.get("selectedPrimaryTag"),
                            "difficulty": difficulty,
                            "slot_delta": difficulty - float(target)
                            if isinstance(target, (int, float)) and difficulty is not None
                            else None,
                            "replacementMode": slot.get("replacementMode"),
                            "generatedBlueprintName": slot.get("generatedBlueprintName"),
                            "transitionPressureSeverity": slot.get("transitionPressureSeverity"),
                            "transitionPressureReason": slot.get("transitionPressureReason"),
                            "transitionPressureScore": slot.get("transitionPressureScore"),
                            "deathsAttributedToSlot": slot.get("deathsAttributedToSlot"),
                        }
                    )


def parse_markov_baseline(source_path: Path) -> dict[tuple[int, int, int, int], float]:
    if not source_path.exists():
        return {}

    tag_index = {name: index for index, name in enumerate(CHUNK_TAGS)}
    band_index = {name: index for index, name in enumerate(DIFFICULTY_BANDS)}
    pattern = re.compile(
        r"SetBaseline\(ChunkTag\.(\w+),\s*ChunkTag\.(\w+),\s*ChunkTag\.(\w+),\s*DifficultyBand\.(\w+),\s*([0-9.]+)f?\)"
    )

    baselines: dict[tuple[int, int, int, int], float] = {}
    for match in pattern.finditer(source_path.read_text(encoding="utf-8")):
        prev2, prev1, next_tag, band, weight = match.groups()
        if prev2 not in tag_index or prev1 not in tag_index or next_tag not in tag_index or band not in band_index:
            continue
        key = (tag_index[prev2], tag_index[prev1], tag_index[next_tag], band_index[band])
        baselines[key] = float(weight)

    return baselines


def load_markov_weights(path: Path) -> dict[tuple[int, int, int, int], float]:
    if not path.exists():
        return {}

    raw = json.loads(path.read_text(encoding="utf-8"))
    weights: dict[tuple[int, int, int, int], float] = {}
    for entry in raw.get("entries", []) or []:
        key = (
            int(entry.get("prev2", 0)),
            int(entry.get("prev1", 0)),
            int(entry.get("next", 0)),
            int(entry.get("band", 0)),
        )
        weights[key] = float(entry.get("weight", 1.0))
    return weights


def name_for_key(key: tuple[int, int, int, int]) -> str:
    prev2, prev1, next_tag, band = key
    prev2_name = CHUNK_TAGS[prev2] if 0 <= prev2 < len(CHUNK_TAGS) else str(prev2)
    prev1_name = CHUNK_TAGS[prev1] if 0 <= prev1 < len(CHUNK_TAGS) else str(prev1)
    next_name = CHUNK_TAGS[next_tag] if 0 <= next_tag < len(CHUNK_TAGS) else str(next_tag)
    band_name = DIFFICULTY_BANDS[band] if 0 <= band < len(DIFFICULTY_BANDS) else str(band)
    return f"{prev2_name} -> {prev1_name} -> {next_name} ({band_name})"


def markov_drift_report(weights_path: Path, source_path: Path) -> tuple[list[str], list[dict[str, Any]]]:
    baseline = parse_markov_baseline(source_path)
    learned = load_markov_weights(weights_path)
    rows: list[dict[str, Any]] = []

    if not learned:
        return [f"No learned Markov weights found at {weights_path}."], rows

    for key, learned_weight in learned.items():
        baseline_weight = baseline.get(key)
        if baseline_weight is None:
            # Match the runtime fallback to the general prev2=Rest baseline where possible.
            baseline_weight = baseline.get((CHUNK_TAGS.index("Rest"), key[1], key[2], key[3]), 1.0)
        delta = learned_weight - baseline_weight
        rows.append(
            {
                "transition": name_for_key(key),
                "baseline": baseline_weight,
                "learned": learned_weight,
                "delta": delta,
                "abs_delta": abs(delta),
            }
        )

    rows.sort(key=lambda row: row["abs_delta"], reverse=True)
    changed = [row for row in rows if row["abs_delta"] >= 0.01]

    lines = [
        f"Learned entries: {len(learned)}",
        f"Baseline entries parsed: {len(baseline)}",
        f"Entries with |delta| >= 0.01: {len(changed)}",
    ]
    if changed:
        avg_abs = avg(row["abs_delta"] for row in changed)
        lines.append(f"Average absolute drift among changed entries: {fmt(avg_abs)}")
        lines.append("Top changed transitions:")
        for row in changed[:10]:
            lines.append(
                f"- {row['transition']}: baseline {fmt(row['baseline'])}, "
                f"learned {fmt(row['learned'])}, delta {fmt(row['delta'])}"
            )

    return lines, rows


def write_markov_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    fields = ["transition", "baseline", "learned", "delta", "abs_delta"]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def write_target_svg(path: Path, label: str, runs: list[dict[str, Any]]) -> None:
    width = 760
    height = 320
    pad = 46
    chart_w = width - (pad * 2)
    chart_h = height - (pad * 2)

    values = []
    for index, run in enumerate(runs, start=1):
        target = metric(run, "targetDifficultyBeforeRun")
        actual = metric(run, "actualLevelDifficultyScore")
        if isinstance(target, (int, float)) and isinstance(actual, (int, float)):
            values.append((index, float(target), float(actual)))

    if not values:
        path.write_text("<svg xmlns='http://www.w3.org/2000/svg'></svg>\n", encoding="utf-8")
        return

    y_min = 0.0
    y_max = max(10.0, max(max(target, actual) for _, target, actual in values) + 0.5)

    def sx(index: int) -> float:
        if len(values) == 1:
            return pad + chart_w / 2
        return pad + ((index - 1) / (len(values) - 1)) * chart_w

    def sy(value: float) -> float:
        return pad + chart_h - ((value - y_min) / (y_max - y_min)) * chart_h

    target_points = " ".join(f"{sx(i):.1f},{sy(target):.1f}" for i, target, _ in values)
    actual_points = " ".join(f"{sx(i):.1f},{sy(actual):.1f}" for i, _, actual in values)

    svg = [
        "<svg xmlns='http://www.w3.org/2000/svg' width='760' height='320' viewBox='0 0 760 320'>",
        "<rect width='760' height='320' fill='white'/>",
        f"<text x='{pad}' y='26' font-family='Arial' font-size='16' font-weight='bold'>Target vs Actual Difficulty - {label}</text>",
        f"<line x1='{pad}' y1='{pad}' x2='{pad}' y2='{height-pad}' stroke='#333'/>",
        f"<line x1='{pad}' y1='{height-pad}' x2='{width-pad}' y2='{height-pad}' stroke='#333'/>",
    ]

    for tick in range(0, int(y_max) + 1, 2):
        y = sy(tick)
        svg.append(f"<line x1='{pad-4}' y1='{y:.1f}' x2='{width-pad}' y2='{y:.1f}' stroke='#e3e3e3'/>")
        svg.append(f"<text x='12' y='{y+4:.1f}' font-family='Arial' font-size='11'>{tick}</text>")

    svg.extend(
        [
            f"<polyline points='{target_points}' fill='none' stroke='#2b6cb0' stroke-width='3'/>",
            f"<polyline points='{actual_points}' fill='none' stroke='#c53030' stroke-width='3'/>",
        ]
    )

    for i, target, actual in values:
        svg.append(f"<circle cx='{sx(i):.1f}' cy='{sy(target):.1f}' r='3' fill='#2b6cb0'/>")
        svg.append(f"<circle cx='{sx(i):.1f}' cy='{sy(actual):.1f}' r='3' fill='#c53030'/>")
        svg.append(f"<text x='{sx(i)-4:.1f}' y='{height-pad+18}' font-family='Arial' font-size='10'>{i}</text>")

    svg.extend(
        [
            f"<rect x='{width-pad-180}' y='18' width='170' height='42' fill='white' stroke='#ccc'/>",
            f"<line x1='{width-pad-168}' y1='34' x2='{width-pad-130}' y2='34' stroke='#2b6cb0' stroke-width='3'/>",
            f"<text x='{width-pad-122}' y='38' font-family='Arial' font-size='12'>Target</text>",
            f"<line x1='{width-pad-168}' y1='52' x2='{width-pad-130}' y2='52' stroke='#c53030' stroke-width='3'/>",
            f"<text x='{width-pad-122}' y='56' font-family='Arial' font-size='12'>Actual</text>",
            "</svg>",
        ]
    )
    path.write_text("\n".join(svg) + "\n", encoding="utf-8")


def write_report(
    path: Path,
    summaries: list[dict[str, Any]],
    datasets: list[tuple[str, list[dict[str, Any]]]],
    markov_lines: list[str],
) -> None:
    lines: list[str] = []
    lines.append("# Emotional Platformer Evaluation Report")
    lines.append("")
    lines.append(f"Generated At: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("")
    lines.append("## Evaluation Framing")
    lines.append("")
    lines.append(
        "This report treats difficulty as a multi-metric construct rather than relying on "
        "`actualLevelDifficultyScore` as objective truth. Evidence is grouped into controller intent, "
        "delivered structural content, and player outcome/behaviour."
    )
    lines.append("")
    lines.append("## Dataset Summary")
    lines.append("")
    lines.append(
        "| Dataset | Generation Mode(s) | Runs | Avg Target | Avg Actual | Avg Actual-Target | Avg Abs Error | "
        "Overshoot > 1 | Avg Deaths | Avg Time/Chunk | Avg Pressure | Avg Generated Slots |"
    )
    lines.append("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for summary in summaries:
        lines.append(
            f"| {summary['label']} | {summary['generation_modes']} | {summary['runs']} | {fmt(summary['avg_target'])} | "
            f"{fmt(summary['avg_actual'])} | {fmt(summary['avg_delta'])} | "
            f"{fmt(summary['avg_abs_delta'])} | {summary['overshoot_gt_1']} | "
            f"{fmt(summary['avg_deaths'])} | {fmt(summary['avg_time_per_chunk'])} | "
            f"{fmt(summary['avg_pressure_count'])} | {fmt(summary['avg_generated_slots'])} |"
        )

    lines.append("")
    lines.append("## Controller Intent")
    lines.append("")
    for label, runs in datasets:
        decisions = Counter(adaptation_metric(run, "decisionCode", "unknown") or "unknown" for run in runs)
        target_changes = [
            (
                adaptation_metric(run, "targetAfter"),
                adaptation_metric(run, "targetBefore"),
            )
            for run in runs
        ]
        increases = sum(
            1
            for after, before in target_changes
            if isinstance(after, (int, float)) and isinstance(before, (int, float)) and after > before
        )
        decreases = sum(
            1
            for after, before in target_changes
            if isinstance(after, (int, float)) and isinstance(before, (int, float)) and after < before
        )
        comfort_runs = sum(1 for run in runs if adaptation_metric(run, "comfortRun") is True)
        low_signal_deaths = sum(1 for run in runs if adaptation_metric(run, "lowSignalDeathRun") is True)
        lines.append(f"### {label}")
        lines.append("")
        lines.append(f"- Target increases: {increases}")
        lines.append(f"- Target decreases: {decreases}")
        if comfort_runs > 0 or low_signal_deaths > 0:
            lines.append(f"- Comfort-run evidence: {comfort_runs} runs, including {low_signal_deaths} low-signal one-death runs")
        lines.append("- Adaptation decisions:")
        for decision, count in decisions.most_common():
            lines.append(f"  - `{decision}`: {count}")
        lines.append("")

    lines.append("## Delivered Structural Content")
    lines.append("")
    for summary in summaries:
        lines.append(f"### {summary['label']}")
        lines.append("")
        lines.append(f"- Average actual-target delta: {fmt(summary['avg_delta'])}")
        lines.append(f"- Average absolute target error: {fmt(summary['avg_abs_delta'])}")
        lines.append(f"- Slot-level average absolute error: {fmt(summary['slot_avg_abs_delta'])}")
        lines.append(f"- Slot-level errors above 1.0: {summary['slot_abs_delta_gt_1']}")
        lines.append(f"- High-pressure transitions: {summary['high_pressure_total']}")
        lines.append(f"- Average generated slots per run: {fmt(summary['avg_generated_slots'])}")
        lines.append("")

    lines.append("## Player Outcome And Behaviour")
    lines.append("")
    lines.append(
        "| Dataset | Avg Deaths | Avg Deaths/Chunk | Avg Time/Chunk | Engagement | "
        "Hesitation | Momentum | Reversals/s |"
    )
    lines.append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")
    for summary in summaries:
        lines.append(
            f"| {summary['label']} | {fmt(summary['avg_deaths'])} | "
            f"{fmt(summary['avg_deaths_per_chunk'])} | {fmt(summary['avg_time_per_chunk'])} | "
            f"{fmt(summary['avg_engagement'])} | {fmt(summary['avg_hesitation'])} | "
            f"{fmt(summary['avg_momentum'])} | {fmt(summary['avg_reversals'])} |"
        )

    lines.append("")
    lines.append("## Markov Learning Audit")
    lines.append("")
    for summary in summaries:
        lines.append(
            f"- {summary['label']}: caps={summary['markov_caps']}, "
            f"transition updates={summary['markov_updates']}, avg learning quality={fmt(summary['avg_markov_quality'])}"
        )
    for line in markov_lines:
        lines.append(f"- {line}" if not line.startswith("-") else line)

    lines.append("")
    lines.append("## Interpretation Notes")
    lines.append("")
    lines.append(
        "- `actualLevelDifficultyScore` should be discussed as a structural estimate, not an objective measure of player difficulty."
    )
    lines.append(
        "- Behavioural values are gameplay proxies for strain/flow disruption, not direct emotion classification."
    )
    lines.append(
        "- Strong evidence comes from agreement between delivered structure, runtime outcomes, and player notes."
    )
    lines.append(
        "- Markov learning should be interpreted conservatively unless weight drift and run-level audit fields show meaningful change."
    )

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    datasets_with_paths = parse_datasets(args)
    loaded = [(label, path, load_runs(path, args.latest)) for label, path in datasets_with_paths]

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_dir = Path(args.out_dir).expanduser().resolve() if args.out_dir else Path.cwd() / "EvaluationReports" / f"evaluation_{timestamp}"
    out_dir.mkdir(parents=True, exist_ok=True)

    datasets = [(label, runs) for label, _, runs in loaded]
    summaries = [summarize_dataset(label, path, runs) for label, path, runs in loaded]

    write_runs_csv(out_dir / "runs_summary.csv", datasets)
    write_slots_csv(out_dir / "slots_summary.csv", datasets)

    for label, runs in datasets:
        write_target_svg(out_dir / f"target_vs_actual_{label}.svg", label, runs)

    markov_lines, markov_rows = markov_drift_report(
        Path(args.markov_weights).expanduser().resolve(),
        Path(args.markov_source).expanduser().resolve(),
    )
    write_markov_csv(out_dir / "markov_weight_drift.csv", markov_rows)
    write_report(out_dir / "report.md", summaries, datasets, markov_lines)

    print(f"Evaluation report written to: {out_dir}")
    print(f"- {out_dir / 'report.md'}")
    print(f"- {out_dir / 'runs_summary.csv'}")
    print(f"- {out_dir / 'slots_summary.csv'}")
    print(f"- {out_dir / 'markov_weight_drift.csv'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
