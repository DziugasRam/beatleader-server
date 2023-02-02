﻿using AngleSharp.Common;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using System.Runtime.ConstrainedExecution;
using static BeatLeader_Server.Utils.ResponseUtils;

namespace BeatLeader_Server.Utils
{
    static class ReplayUtils
    {
        static List<(double, double)> pointList = new List<(double, double)> { 
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.101),
                (0.955, 1.047),
                (0.95, 1.000),
                (0.94, 0.919),
                (0.93, 0.847),
                (0.92, 0.786),
                (0.91, 0.734),
                (0.9, 0.692),
                (0.875, 0.606),
                (0.85, 0.537),
                (0.825, 0.480),
                (0.8, 0.429),
                (0.75, 0.345),
                (0.7, 0.286),
                (0.65, 0.246),
                (0.6, 0.217),
                (0.0, 0.000) };
        public static float Curve(float acc, float stars)
        {
            float l = (float)(1f - (0.03f * (stars - 3.0f) / 11.0f));
            float a = 0.96f * l;
            float f = 1.2f - 0.6f * stars / 14.0f;

            return MathF.Pow(MathF.Log10(l / (l - acc)) / MathF.Log10(l / (l - a)), f);
        }

        public static float Curve2(float acc)
        {
            int i = 0;
            for (; i < pointList.Count; i++)
            {
                if (pointList[i].Item1 <= acc) {
                    break;
                }
            }
    
            if (i == 0) {
                i = 1;
            }
    
            double middle_dis = (acc - pointList[i-1].Item1) / (pointList[i].Item1 - pointList[i-1].Item1);
            return (float)(pointList[i-1].Item2 + middle_dis * (pointList[i].Item2 - pointList[i-1].Item2));
        }

        public static float AccRating(float? predictedAcc, float? passRating, float? techRating) {
            float difficulty_to_acc;
            if (predictedAcc > 0) {
                difficulty_to_acc = 15f / Curve2((predictedAcc ?? 0) + 0.005f);
            } else {
                float tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                difficulty_to_acc = (-MathF.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
            }
            if (float.IsInfinity(difficulty_to_acc) || float.IsNaN(difficulty_to_acc) || float.IsNegativeInfinity(difficulty_to_acc)) {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        private static float Inflate(float peepee) {
            return (443.12f * MathF.Pow(peepee, 1.3f)) / MathF.Pow(443.12f, 1.3f) * 1.081805f;
        }

        private static float Inflate2(float peepee) {
            return peepee + peepee / MathF.Pow(MathF.E, (-peepee + 800)/150);
        }

        private static float Lack(float passPP, float accPP, float techPP) {
            float refinedAccPP = accPP + techPP;
            float bonusPP = MathF.Sqrt(MathF.Pow(passPP, 2) + MathF.Pow(refinedAccPP, 2)) * passPP * refinedAccPP / (passPP + refinedAccPP) / 250;
            return (bonusPP + refinedAccPP) * 1.111f;
        }

        private static (float, float, float) GetPp(float accuracy, float predictedAcc, float passRating, float techRating) {
            float difficulty_to_acc = AccRating(predictedAcc, passRating, techRating);

            float reducedTechRating = techRating / 10;
            float passPP = passRating * 13.5f;
            float accPP = Curve2(accuracy) * difficulty_to_acc * 28.5f;
            float techPP = (float)(1 / (1 + Math.Pow(Math.E, -32 * (accuracy - 0.925f))) * (reducedTechRating / (1 + Math.Pow(Math.E, -8 * (reducedTechRating - 0.25f)))) * 8 * difficulty_to_acc);
            
            return (passPP, accPP, techPP);
        }

        public static (float, float, float, float, float) PpFromScore(
            float accuracy, 
            string modifiers, 
            ModifiersMap modifierValues, 
            ModifiersRating? modifiersRating,
            float predictedAcc, 
            float passRating, 
            float techRating, 
            bool timing)
        {
            bool negativeAcc = float.IsNegative(accuracy);
            if (negativeAcc)
            {
                accuracy *= -1;
            }

            float mp = modifierValues.GetTotalMultiplier(modifiers, modifiersRating == null);

            float rawPP = 0; float fullPP = 0; float passPP = 0; float accPP = 0; float techPP = 0;
            if (!timing) {
                if (!modifiers.Contains("NF"))
                {
                    (passPP, accPP, techPP) = GetPp(accuracy, predictedAcc, passRating, techRating);
                    rawPP = Inflate(passPP + accPP + techPP);
                    if (modifiersRating != null) {
                        var modifiersMap = modifiersRating.ToDictionary<float>();
                        foreach (var modifier in modifiers.ToUpper().Split(","))
                        {
                            if (modifiersMap.ContainsKey(modifier + "PredictedAcc")) { 
                                predictedAcc = modifiersMap[modifier + "PredictedAcc"]; 
                                passRating = modifiersMap[modifier + "PassRating"]; 
                                techRating = modifiersMap[modifier + "TechRating"]; 

                                break;
                            }
                        }
                    }
                    (passPP, accPP, techPP) = GetPp(accuracy, predictedAcc, passRating * mp, techRating * mp);
                    fullPP = Inflate(passPP + accPP + techPP);
                }
            } else {
                rawPP = accuracy * passRating * 55f;
                fullPP = accuracy * passRating * 55f;
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

            return (fullPP, fullPP - rawPP, passPP, accPP, techPP);
        }

        public static (float, float, float, float, float) PpFromScore(Score s, DifficultyDescription difficulty) {
            return PpFromScore(
                s.Accuracy, 
                s.Modifiers, 
                difficulty.ModifierValues, 
                difficulty.ModifiersRating,
                difficulty.PredictedAcc ?? 0.0f, 
                difficulty.PassRating ?? 0.0f, 
                difficulty.TechRating ?? 0.0f, 
                difficulty.ModeName.ToLower() == "rhythmgamestandard");
        }

        public static (float, float, float, float, float) PpFromScoreResponse(
            ScoreResponse s, 
            float predictedAcc, 
            float passRating, 
            float techRating, 
            ModifiersMap modifiers,
            ModifiersRating? modifiersRating)
        {
            return PpFromScore(s.Accuracy, s.Modifiers, modifiers, modifiersRating, predictedAcc, passRating, techRating, false);
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
                (score.Pp, score.BonusPp, score.PassPP, score.AccPP, score.TechPP) = PpFromScore(score, difficulty);
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
            string lowerHmd = controllerName.ToLower();

            if (lowerHmd.Contains("vive tracker") && lowerHmd.Contains("3")) return ControllerEnum.viveTracker3;
            if (lowerHmd.Contains("vive tracker") && lowerHmd.Contains("pro")) return ControllerEnum.viveTracker2;
            if (lowerHmd.Contains("vive tracker")) return ControllerEnum.viveTracker;

            if (lowerHmd.Contains("vive") && lowerHmd.Contains("cosmos")) return ControllerEnum.viveCosmos;
            if (lowerHmd.Contains("vive") && lowerHmd.Contains("pro") && lowerHmd.Contains("2")) return ControllerEnum.vivePro2;
            if (lowerHmd.Contains("vive") && lowerHmd.Contains("pro")) return ControllerEnum.vivePro;
            if (lowerHmd.Contains("vive")) return ControllerEnum.vive;

            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("phoenix")) return ControllerEnum.picophoenix;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("3")) return ControllerEnum.picoNeo3;
            if (lowerHmd.Contains("pico neo") && lowerHmd.Contains("2")) return ControllerEnum.picoNeo2;
            if (lowerHmd.Contains("knuckles")) return ControllerEnum.knuckles;
            if (lowerHmd.Contains("miramar")) return ControllerEnum.miramar;
            
            if (lowerHmd.Contains("quest pro")) return ControllerEnum.questPro;
            if (lowerHmd.Contains("quest2")) return ControllerEnum.quest2;
            if (lowerHmd.Contains("oculus touch") || lowerHmd.Contains("rift cv1")) return ControllerEnum.oculustouch;
            if (lowerHmd.Contains("rift s") || lowerHmd.Contains("quest")) return ControllerEnum.oculustouch2;

            if (lowerHmd.Contains("windows")) return ControllerEnum.wmr;
            if (lowerHmd.Contains("nolo")) return ControllerEnum.nolo;
            if (lowerHmd.Contains("disco")) return ControllerEnum.disco;
            if (lowerHmd.Contains("hands")) return ControllerEnum.hands;

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

        public static float GetTotalMultiplier(this ModifiersMap modifiersObject, string modifiers, bool speedModifiers)
		{
			float multiplier = 1;

            var modifiersMap = modifiersObject.ToDictionary<float>();
            foreach (var modifier in modifiersMap.Keys)
            {
                if (!speedModifiers && (modifier == "SF" || modifier == "SS" || modifier == "FS")) continue;

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
    }
}
