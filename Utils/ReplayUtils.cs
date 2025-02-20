﻿using AngleSharp.Common;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using System.Runtime.ConstrainedExecution;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Utils
{
    
    static class ReplayUtils
    {
        public static float Curve(float acc, float stars)
        {
            float l = (float)(1f - (0.03f * (stars - 3.0f) / 11.0f));
            float a = 0.96f * l;
            float f = 1.2f - 0.6f * stars / 14.0f;

            return MathF.Pow(MathF.Log10(l / (l - acc)) / MathF.Log10(l / (l - a)), f);
        }

        public static (float, float) PpFromScore(float accuracy, string modifiers, ModifiersMap modifierValues, float stars, bool timing)
        {
            bool negativeAcc = float.IsNegative(accuracy);
            if (negativeAcc)
            {
                accuracy *= -1;
            }

            float mp = modifierValues.GetTotalMultiplier(modifiers);

            float rawPP = 0; float fullPP = 0;
            if (!timing) {
                if (!modifiers.Contains("NF"))
                {
                    rawPP = (float)(Curve(accuracy, (float)stars - 0.5f) * ((float)stars + 0.5f) * 42);
                    fullPP = (float)(Curve(accuracy, (float)stars * mp - 0.5f) * ((float)stars * mp + 0.5f) * 42);
                }
            } else {
                rawPP = accuracy * stars * 55f;
                fullPP = accuracy * stars * mp * 55f;
            }

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP))
            {
                rawPP = 1042;

            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP))
            {
                fullPP = 1042;

            }

            if (negativeAcc)
            {

                rawPP *= -1;
                fullPP *= -1;
            }

            return (fullPP, fullPP - rawPP);
        }

        public static (float, float) PpFromScore(Score s, DifficultyDescription difficulty) {
            return PpFromScore(s.Accuracy, s.Modifiers, difficulty.ModifierValues, difficulty.Stars ?? 0.0f, difficulty.ModeName.ToLower() == "rhythmgamestandard");
        }

        public static (float, float) PpFromScoreResponse(ScoreResponse s, float stars, ModifiersMap modifiers)
        {
            var accuracy = s.Accuracy;
            bool negativeAcc = float.IsNegative(accuracy);
            if (negativeAcc)
            {
                accuracy *= -1;
            }

            float mp = modifiers.GetPositiveMultiplier(s.Modifiers);

            float rawPP = 0; float fullPP = 0;
            if (!s.Modifiers.Contains("NF")) {
                rawPP = (float)(Curve(accuracy, (float)stars - 0.5f) * ((float)stars + 0.5f) * 42);
                fullPP = (float)(Curve(accuracy, (float)stars * mp - 0.5f) * ((float)stars * mp + 0.5f) * 42);
            }

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP))
            {
                rawPP = 1042;
            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP))
            {
                fullPP = 1042;
            }

            if (negativeAcc)
            {

                rawPP *= -1;
                fullPP *= -1;
            }

            return (fullPP, fullPP - rawPP);
        }

        public static (Score, int) ProcessReplayInfo(ReplayInfo info, DifficultyDescription difficulty) {
            Score score = new Score();
            
            score.BaseScore = info.score;
            score.Modifiers = info.modifiers;
            score.Hmd = HMDFromName(info.hmd);
            score.Controller = ControllerFromName(info.controller);

            var status = difficulty.Status;
            var modifers = difficulty.ModifierValues ?? new ModifiersMap();
            bool qualification = status == DifficultyStatus.qualified || status == DifficultyStatus.inevent;
            bool hasPp = status == DifficultyStatus.ranked || qualification;

            int maxScore = difficulty.MaxScore > 0 ? difficulty.MaxScore : MaxScoreForNote(difficulty.Notes);
            if (hasPp)
            {
                score.ModifiedScore = (int)(score.BaseScore * modifers.GetNegativeMultiplier(info.modifiers));
            } else
            {
                score.ModifiedScore = (int)((score.BaseScore + (int)((float)(maxScore - score.BaseScore) * (modifers.GetPositiveMultiplier(info.modifiers) - 1))) * modifers.GetNegativeMultiplier(info.modifiers));
            }
            
            score.Accuracy = (float)score.BaseScore / (float)maxScore;
            score.Modifiers = info.modifiers;

            if (hasPp) {
                (score.Pp, score.BonusPp) = PpFromScore(score, difficulty);
            }

            score.Qualification = qualification;
            score.Platform = info.platform + "," + info.gameVersion + "," + info.version;
            score.Timeset = info.timestamp;
            score.IgnoreForStats = difficulty.ModeName.ToLower() == "rhythmgamestandard" || info.modifiers.Contains("NF");
            score.Migrated = true;
            
            return (score, maxScore);
        }

        public static void PostProcessReplay(Score score, Replay replay) {
            score.WallsHit = replay.walls.Count;
            float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
            float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;
            score.Pauses = replay.pauses.Where(p => p.time >= firstNoteTime && p.time <= lastNoteTime).Count();
            foreach (var item in replay.notes)
            {
                switch (item.eventType)
                {
                    case NoteEventType.bad:
                        score.BadCuts++;
                        break;
                    case NoteEventType.miss:
                        score.MissedNotes++;
                        break;
                    case NoteEventType.bomb:
                        score.BombCuts++;
                        break;
                    default:
                        break;
                }
            }
            score.FullCombo = score.BombCuts == 0 && score.MissedNotes == 0 && score.WallsHit == 0 && score.BadCuts == 0;
        }

        public static HMD HMDFromName(string hmdName) {
            string lowerHmd = hmdName.ToLower();

            if (lowerHmd.Contains("pico") && lowerHmd.Contains("4")) return HMD.picoNeo4;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("3")) return HMD.picoNeo3;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("2")) return HMD.picoNeo2;
            if (lowerHmd.Contains("vive pro 2")) return HMD.vivePro2;
            if (lowerHmd.Contains("vive elite")) return HMD.viveElite;
            if (lowerHmd.Contains("focus3")) return HMD.viveFocus;
            if (lowerHmd.Contains("miramar")) return HMD.miramar;
            if (lowerHmd.Contains("pimax vision 8k")) return HMD.pimax8k;
            if (lowerHmd.Contains("pimax 5k")) return HMD.pimax5k;
            if (lowerHmd.Contains("pimax artisan")) return HMD.pimaxArtisan;

            if (lowerHmd.Contains("hp reverb")) return HMD.hpReverb;
            if (lowerHmd.Contains("samsung windows")) return HMD.samsungWmr;
            if (lowerHmd.Contains("qiyu dream")) return HMD.qiyuDream;
            if (lowerHmd.Contains("disco")) return HMD.disco;
            if (lowerHmd.Contains("lenovo explorer")) return HMD.lenovoExplorer;
            if (lowerHmd.Contains("acer ah1010")) return HMD.acerWmr;
            if (lowerHmd.Contains("acer ah5010")) return HMD.acerWmr;
            if (lowerHmd.Contains("arpara")) return HMD.arpara;
            if (lowerHmd.Contains("dell visor")) return HMD.dellVisor;

            if (lowerHmd.Contains("e3")) return HMD.e3;
            if (lowerHmd.Contains("vive dvt")) return HMD.viveDvt;
            if (lowerHmd.Contains("3glasses s20")) return HMD.glasses20;
            if (lowerHmd.Contains("hedy")) return HMD.hedy;
            if (lowerHmd.Contains("vaporeon")) return HMD.vaporeon;
            if (lowerHmd.Contains("huaweivr")) return HMD.huaweivr;
            if (lowerHmd.Contains("asus mr0")) return HMD.asusWmr;
            if (lowerHmd.Contains("cloudxr")) return HMD.cloudxr;
            if (lowerHmd.Contains("vridge")) return HMD.vridge;
            if (lowerHmd.Contains("medion mixed reality")) return HMD.medion;

            if (lowerHmd.Contains("quest") && lowerHmd.Contains("2")) return HMD.quest2;
            if (lowerHmd.Contains("quest") && lowerHmd.Contains("pro")) return HMD.questPro;

            if (lowerHmd.Contains("vive cosmos")) return HMD.viveCosmos;
            if (lowerHmd.Contains("vive_cosmos")) return HMD.viveCosmos;
            if (lowerHmd.Contains("index")) return HMD.index;
            if (lowerHmd.Contains("quest")) return HMD.quest;
            if (lowerHmd.Contains("rift s")) return HMD.riftS;
            if (lowerHmd.Contains("rift_s")) return HMD.riftS;
            if (lowerHmd.Contains("windows")) return HMD.wmr;
            if (lowerHmd.Contains("vive pro")) return HMD.vivePro;
            if (lowerHmd.Contains("vive_pro")) return HMD.vivePro;
            if (lowerHmd.Contains("vive")) return HMD.vive;
            if (lowerHmd.Contains("rift")) return HMD.rift;

            return HMD.unknown;
        }

        public static ControllerEnum ControllerFromName(string controllerName) {
            string lowerController = controllerName.ToLower();

            if (lowerController.Contains("vive tracker") && lowerController.Contains("3")) return ControllerEnum.viveTracker3;
            if (lowerController.Contains("vive tracker") && lowerController.Contains("pro")) return ControllerEnum.viveTracker2;
            if (lowerController.Contains("vive tracker")) return ControllerEnum.viveTracker;

            if (lowerController.Contains("vive") && lowerController.Contains("cosmos")) return ControllerEnum.viveCosmos;
            if (lowerController.Contains("vive") && lowerController.Contains("pro") && lowerController.Contains("2")) return ControllerEnum.vivePro2;
            if (lowerController.Contains("vive") && lowerController.Contains("pro")) return ControllerEnum.vivePro;
            if (lowerController.Contains("vive")) return ControllerEnum.vive;

            if (lowerController.Contains("pico neo") && lowerController.Contains("phoenix")) return ControllerEnum.picophoenix;
            if (lowerController.Contains("pico neo") && lowerController.Contains("3")) return ControllerEnum.picoNeo3;
            if (lowerController.Contains("pico neo") && lowerController.Contains("2")) return ControllerEnum.picoNeo2;
            if (lowerController.Contains("knuckles")) return ControllerEnum.knuckles;
            if (lowerController.Contains("miramar")) return ControllerEnum.miramar;
            
            if (lowerController.Contains("quest pro")) return ControllerEnum.questPro;
            if (lowerController.Contains("quest2")) return ControllerEnum.quest2;
            if (lowerController.Contains("oculus touch") || lowerController.Contains("rift cv1")) return ControllerEnum.oculustouch;
            if (lowerController.Contains("rift s") || lowerController.Contains("quest")) return ControllerEnum.oculustouch2;

            if (lowerController.Contains("windows")) return ControllerEnum.wmr;
            if (lowerController.Contains("nolo")) return ControllerEnum.nolo;
            if (lowerController.Contains("disco")) return ControllerEnum.disco;
            if (lowerController.Contains("hands")) return ControllerEnum.hands;

            return ControllerEnum.unknown;
        }

        public static int MaxScoreForNote(int count) {
          int note_score = 115;

          if (count <= 1) // x1 (+1 note)
              return note_score * (0 + (count - 0) * 1);
          if (count <= 5) // x2 (+4 notes)
              return note_score * (1 + (count - 1) * 2);
          if (count <= 13) // x4 (+8 notes)
              return note_score * (9 + (count - 5) * 4);
          // x8
          return note_score * (41 + (count - 13) * 8);
        }

        public static float GetTotalMultiplier(this ModifiersMap modifiersObject, string modifiers)
		{
			float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier)) { multiplier += modifiersMap[modifier]; }
            }
            

			return multiplier;
		}

        public static float GetPositiveMultiplier(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] > 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static float GetNegativeMultiplier(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier) && modifiersMap[modifier] < 0) { multiplier += modifiersMap[modifier]; }
            }

            return multiplier;
        }

        public static (string, float) GetNegativeMultipliers(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;
            List<string> modifierArray = new List<string>();

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (modifiers.Contains(modifier)) {
                    if (modifiersMap[modifier] < 0) {
                        multiplier += modifiersMap[modifier]; 
                        modifierArray.Add(modifier);
                    }
                }
            }

            return (String.Join(",", modifierArray), multiplier);
        }

        public static Dictionary<string, float> LegacyModifiers()
        {
            return new Dictionary<string, float>
            {
                ["DA"] = 0.005f,
                ["FS"] = 0.11f,
                ["SS"] = -0.3f,
                ["SF"] = 0.25f,
                ["GN"] = 0.04f,
                ["NA"] = -0.3f,
                ["NB"] = -0.2f,
                ["NF"] = -0.5f,
                ["NO"] = -0.2f,
                ["PM"] = 0.0f,
                ["SC"] = 0.0f
            };
        }

        public static string? RemoveDuplicates(Replay replay, Leaderboard leaderboard) {
            var groups = replay.notes.GroupBy(n => n.noteID + "_" + n.spawnTime).Where(g => g.Count() > 1).ToList();

            if (groups.Count > 0) {
                int sliderCount = 0;
                foreach (var group in groups)
                {
                    bool slider = false;

                    var toRemove = group.OrderByDescending(n => {
                        NoteParams param = new NoteParams(n.noteID);
                        if (param.scoringType != ScoringType.Default && param.scoringType != ScoringType.Normal) {
                            slider = true;
                        }
                        return ReplayStatisticUtils.ScoreForNote(n, param.scoringType);
                    }).Skip(1).ToList();

                    if (slider) {
                        sliderCount++;
                        continue;
                    }

                    foreach (var removal in toRemove)
                    {
                        replay.notes.Remove(removal);
                    }
                }
                if (sliderCount == groups.Count) return null;
                string? error = null;
                ScoreStatistic? statistic = null;
                try
                {
                    (statistic, error) = ReplayStatisticUtils.ProcessReplay(replay, leaderboard);
                } catch (Exception e) {
                    return error = e.ToString();
                }

                if (statistic != null) {
                    replay.info.score = statistic.winTracker.totalScore;
                } else {
                    return error;
                }
            }
            
            return null;
        }

        public static bool IsPlayerCuttingNotesOnPlatform(Replay replay) {
            if (replay.notes.Count < 20) return true;

            int noteIndex = 0;
            int zSum = 0;

            foreach (var frame in replay.frames)
            {
                if (frame.time >= replay.notes[noteIndex].eventTime) {
                    if (frame.head.position.z > 1.05) {
                        zSum++;
                    }
                    if (zSum == (int)Math.Min(50, replay.notes.Count * 0.1f)) return false;

                    if (noteIndex + 1 != replay.notes.Count) {
                        noteIndex++;
                    } else {
                        break;
                    }
                }
            }

            return true;
        }
    }
}
