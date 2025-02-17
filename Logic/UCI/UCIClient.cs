﻿
#pragma warning disable CS0162 // Unreachable code detected

using System.Reflection;

using Lizard.Logic.NN;
using Lizard.Logic.Threads;

namespace Lizard.Logic.UCI
{
    public unsafe class UCIClient
    {

        private Position pos;
        private SearchInformation info;
        private ThreadSetup setup;

        private static readonly string LogFileName = $".\\ucilog_{ProcessID}.txt";

        private static Dictionary<string, UCIOption> Options;

        private static object LogFileLock = new object();

        public static bool Active = false;

        public UCIClient()
        {
            ProcessUCIOptions();

            setup = new ThreadSetup();
            pos = new Position(owner: GlobalSearchPool.MainThread);
            info = new SearchInformation(pos, DefaultSearchDepth);
            info.OnDepthFinish = OnDepthDone;
            info.OnSearchFinish = OnSearchDone;
            if (File.Exists(LogFileName))
            {
                LogString("\n\n**************************************************\n"
                    + CenteredString(DateTime.Now.ToString(), 50) + "\n"
                    + "**************************************************");
            }
        }

        /// <summary>
        /// Writes the string <paramref name="s"/> to standard output, which will be received by chess UCI's.
        /// </summary>
        public static void SendString(string s)
        {
            Console.WriteLine(s);
            LogString("[OUT]: " + s);
        }

        /// <summary>
        /// Appends the string <paramref name="s"/> to the file <see cref="LogFileName"/>
        /// </summary>
        public static void LogString(string s, bool newLine = true)
        {
            if (NO_LOG_FILE)
            {
                return;
            }

            lock (LogFileLock)
            {
                try
                {
                    string fileToWrite = LogFileName;

                    using FileStream fs = new FileStream(fileToWrite, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using StreamWriter sw = new StreamWriter(fs);

                    if (newLine)
                    {
                        sw.WriteLine(s);
                    }
                    else
                    {
                        sw.Write(s);
                    }

                    sw.Flush();
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR LogString('" + s + "') failed!");
                    Console.WriteLine(e.ToString());
                }
            }
        }

        /// <summary>
        /// Blocks until a command is sent in the standard input stream
        /// </summary>
        /// <param name="cmd">Set to the command, which is the first word in the input</param>
        /// <returns>The remaining words in the input, which are parameters for the command</returns>
        private static string[] ReceiveString(out string cmd)
        {
            string input = Console.ReadLine();
            if (input == null || input.Length == 0)
            {
                cmd = ":(";
                return Array.Empty<string>();
            }

            string[] splits = input.Split(" ");
            cmd = splits[0].ToLower();
            string[] param = splits.ToList().GetRange(1, splits.Length - 1).ToArray();

            LogString("[IN]: " + input);

            return param;
        }

        /// <summary>
        /// Sends the UCI options, and begins waiting for input.
        /// </summary>
        public void Run()
        {
            Active = true;

#if DEV
            SendString("id name Lizard " + EngineBuildVersion + " DEV");
#else
            SendString("id name Lizard " + EngineBuildVersion);
#endif
            SendString("id author Liam McGuire");
            SendString("info string Using Bucketed768 evaluation.");

            foreach (string k in Options.Keys)
            {
                SendString(Options[k].ToString());
            }
            SendString("uciok");

            LogString("[INFO]: Compiler info -> '" + GetCompilerInfo() + "'");

            //  In case a "ucinewgame" isn't sent for the first game
            HandleNewGame(pos.Owner.AssocPool);
            InputLoop();
        }

        /// <summary>
        /// Handles commands sent by UCI's.
        /// </summary>
        private void InputLoop()
        {
            while (true)
            {
                string[] param = ReceiveString(out string cmd);

                if (cmd == "quit")
                {
                    LogString("[INFO]: Exiting with code " + 0);
                    Environment.Exit(0);
                }
                else if (cmd == "isready")
                {
                    SendString("readyok");
                }
                else if (cmd == "ucinewgame")
                {
                    HandleNewGame(GlobalSearchPool);
                }
                else if (cmd == "position")
                {
                    pos.IsChess960 = UCI_Chess960;

                    info = new SearchInformation(pos, DefaultSearchDepth);
                    info.OnDepthFinish = OnDepthDone;
                    info.OnSearchFinish = OnSearchDone;

                    setup.SetupMoves.Clear();

                    if (param[0] == "startpos")
                    {
                        setup.StartFEN = InitialFEN;

                        //  Some UCI's send commands that look like "position startpos moves e2e4 c7c5 g1f3"
                        //  If the command does have a "moves" component, then set the fen normally,
                        //  and try to make the moves that we were told to.
                        info.Position.LoadFromFEN(setup.StartFEN);
                        if (param.Length > 1 && param[1] == "moves")
                        {
                            for (int i = 2; i < param.Length; i++)
                            {
                                if (info.Position.TryFindMove(param[i], out Move m))
                                {
                                    info.Position.MakeMove(m);
                                }
                                else
                                {
                                    LogString("[ERROR]: Failed doing extra moves! '" + param[i] + "' didn't work with FEN " + info.Position.GetFEN());
                                }

                                setup.SetupMoves.Add(m);
                            }

                            LogString("[INFO]: New FEN is " + info.Position.GetFEN());
                        }
                        else
                        {
                            LogString("[INFO]: Set position to " + InitialFEN);
                        }
                    }
                    else
                    {
                        string fen = param[1];

                        bool hasExtraMoves = false;
                        for (int i = 2; i < param.Length; i++)
                        {
                            if (param[i] == "moves")
                            {
                                info.Position.LoadFromFEN(fen);
                                for (int j = i + 1; j < param.Length; j++)
                                {

                                    if (info.Position.TryFindMove(param[j], out Move m))
                                    {
                                        info.Position.MakeMove(m);
                                    }
                                    else
                                    {
                                        LogString("[ERROR]: Failed doing extra moves! '" + param[j] + "' didn't work with FEN " + info.Position.GetFEN());
                                    }

                                    setup.SetupMoves.Add(m);
                                }

                                LogString("[INFO]: New FEN is " + info.Position.GetFEN());
                                hasExtraMoves = true;
                                break;
                            }
                            else
                            {
                                fen += " " + param[i];
                            }

                        }

                        setup.StartFEN = fen;

                        if (!hasExtraMoves)
                        {
                            LogString("[INFO]: Set position to " + fen);
                            info.Position.LoadFromFEN(fen);
                        }

                    }

                    NNUE.RefreshAccumulator(info.Position);
                }
                else if (cmd == "go")
                {
                    GlobalSearchPool.StopThreads = false;
                    HandleGo(param);
                }
                else if (cmd == "stop")
                {
                    GlobalSearchPool.StopThreads = true;
                }
                else if (cmd == "leave")
                {
                    LogString("[INFO]: Leaving");
                    Active = false;
                    return;
                }
                else if (cmd == "setoption")
                {
                    try
                    {
                        //  param[0] == "name"
                        string optName = param[1];
                        string optValue = "";

                        for (int i = 2; i < param.Length; i++)
                        {
                            if (param[i] == "value")
                            {
                                for (int j = i + 1; j < param.Length; j++)
                                {
                                    optValue += param[j] + " ";
                                }
                                optValue = optValue.Trim();
                                break;
                            }
                            else
                            {
                                optName += " " + param[i];
                            }
                        }

                        //  param[2] == "value"
                        HandleSetOption(optName, optValue);
                    }
                    catch (Exception e)
                    {
                        LogString("[ERROR]: Failed parsing setoption command, got '" + param.ToString() + "'");
                        LogString(e.ToString());
                    }

                }
                else if (cmd == "tune")
                {
                    PrintSPSAParams();
                }
            }
        }




        //  https://gist.github.com/DOBRO/2592c6dad754ba67e6dcaec8c90165bf

        /// <summary>
        /// Process "go" command parameters and begin a search.
        /// 
        /// <para> Currently handled: </para>
        /// <br> movetime -> search for milliseconds </br>
        /// <br> depth -> search until a specific depth (in plies) </br>
        /// <br> nodes -> only look at a maximum number of nodes </br>
        /// <br> infinite -> keep looking until we get a "stop" command </br>
        /// <br> (w/b)time -> the specified player has x amount of milliseconds left</br>
        /// <br> (w/b)inc -> the specified player gains x milliseconds after they move</br>
        /// 
        /// <para> Currently ignored: </para>
        /// <br> ponder, movestogo, mate </br>
        /// 
        /// </summary>
        /// <param name="param">List of parameters sent with the "go" command.</param>
        private void HandleGo(string[] param)
        {
            if (info.SearchActive)
            {
                LogString("[WARN]: Got 'go' command while a search is already in progress, ignoring");
                return;
            }

            TimeManager tm = info.TimeManager;

            //  Assume that we can search infinitely, and let the UCI's "go" parameters constrain us accordingly.
            info.MaxNodes = MaxSearchNodes;
            tm.MaxSearchTime = MaxSearchTime;
            info.MaxDepth = MaxDepth;

            if (info.SearchFinishedCalled)
            {
                info.SearchFinishedCalled = false;
                LogString("[INFO]: Reusing old SearchInfo object, info.SearchFinishedCalled was true");
            }

            bool isMoveTimeCommand = false;
            bool hasPlayerTime = false;

            for (int i = 0; i < param.Length; i++)
            {
                if (param[i] == "movetime")
                {
                    info.SetMoveTime(int.Parse(param[i + 1]));
                    LogString("[INFO]: MaxSearchTime is set to " + tm.MaxSearchTime);

                    isMoveTimeCommand = true;
                }
                else if (param[i] == "depth")
                {
                    if (i + 1 >= param.Length)
                    {
                        break;
                    }
                    if (int.TryParse(param[i + 1], out int reqDepth))
                    {
                        info.MaxDepth = reqDepth;
                        LogString("[INFO]: MaxDepth is set to " + info.MaxDepth);
                    }

                }
                else if (param[i] == "nodes")
                {
                    if (i + 1 >= param.Length)
                    {
                        break;
                    }
                    if (ulong.TryParse(param[i + 1], out ulong reqNodes))
                    {
                        info.MaxNodes = reqNodes;
                        LogString("[INFO]: MaxNodes is set to " + info.MaxNodes);
                    }
                }
                else if (param[i] == "infinite")
                {
                    Assert(info.MaxNodes == MaxSearchNodes, $"go infinite should have MaxNodes == {MaxSearchNodes}, but it was {info.MaxNodes}");
                    Assert(tm.MaxSearchTime == MaxSearchTime, $"go infinite should have MaxSearchTime == {MaxSearchTime}, but it was {tm.MaxSearchTime}");
                    Assert(info.MaxDepth == MaxDepth, $"go infinite should have MaxDepth == {MaxDepth}, but it was {info.MaxDepth}");

                    info.MaxNodes = MaxSearchNodes;
                    tm.MaxSearchTime = MaxSearchTime;
                    info.MaxDepth = MaxDepth;
                }
                else if ((param[i] == "wtime" && info.Position.ToMove == Color.White) ||
                         (param[i] == "btime" && info.Position.ToMove == Color.Black))
                {
                    tm.PlayerTime = int.Parse(param[i + 1]);
                    hasPlayerTime = true;
                }
                else if ((param[i] == "winc" && info.Position.ToMove == Color.White) ||
                         (param[i] == "binc" && info.Position.ToMove == Color.Black))
                {
                    tm.PlayerIncrement = int.Parse(param[i + 1]);
                }
                else if (param[i] == "movestogo")
                {
                    tm.MovesToGo = int.Parse(param[i + 1]);
                }
            }

            //  If we weren't told to search for a specific time (no "movetime" and not "infinite"),
            //  then we make one ourselves
            if (!isMoveTimeCommand && hasPlayerTime)
            {
                info.TimeManager.MakeMoveTime();
            }

            GlobalSearchPool.StartSearch(info.Position, ref info, setup);
        }


        /// <summary>
        /// Sends the "info depth (number) ..." string to the UCI
        /// </summary>
        private void OnDepthDone(ref SearchInformation info)
        {
            SendString(FormatSearchInformationMultiPV(ref info));
        }



        private void OnSearchDone(ref SearchInformation info)
        {
            info.SearchActive = false;

            if (info.SearchFinishedCalled)
            {
                LogString("[INFO]: SearchFinishedCalled was true, ignoring.");
                return;
            }

            info.SearchFinishedCalled = true;
            var bestThread = info.Position.Owner.AssocPool.GetBestThread();
            if (bestThread.RootMoves.Count == 0)
            {
                SendString("bestmove 0000");
                return;
            }

            Move bestThreadMove = bestThread.RootMoves[0].Move;
            if (bestThreadMove.IsNull())
            {
                ScoredMove* legal = stackalloc ScoredMove[MoveListSize];
                int size = info.Position.GenLegal(legal);
                bestThreadMove = legal[0].Move;
            }

            SendString("bestmove " + bestThreadMove.ToString(info.Position.IsChess960));
        }

        private static void HandleNewGame(SearchThreadPool pool)
        {
            pool.MainThread.WaitForThreadFinished();
            pool.TTable.Clear();
            pool.Clear();
        }

        private void HandleSetOption(string optName, string optValue)
        {
            foreach (var key in Options.Keys)
            {
                if (key.Replace(" ", string.Empty).EqualsIgnoreCase(optName.Replace(" ", string.Empty)))
                {
                    try
                    {
                        UCIOption opt = Options[key];
                        object prevValue = opt.FieldHandle.GetValue(null);

                        if (opt.FieldHandle.FieldType == typeof(bool))
                        {
                            bool newValue = true;
                            if (bool.TryParse(optValue, out bool tf))
                            {
                                newValue = tf;
                            }
                            else if (optValue == "1")
                            {
                                newValue = true;
                            }
                            else if (optValue == "0")
                            {
                                newValue = false;
                            }
                            else
                            {
                                LogString("[ERROR]: setoption commands for booleans need to have a \"value\" of 'True/False' or '1/0', " +
                                    "and '" + optValue + "' wasn't one of those!");
                                return;
                            }

                            opt.FieldHandle.SetValue(null, newValue);
                        }
                        else if (opt.FieldHandle.FieldType == typeof(int))
                        {
                            if (int.TryParse(optValue, out int newValue))
                            {
                                if (newValue >= opt.MinValue && newValue <= opt.MaxValue)
                                {
                                    opt.FieldHandle.SetValue(null, newValue);

                                    if (opt.Name == nameof(Threads))
                                    {
                                        GlobalSearchPool.Resize(SearchOptions.Threads);
                                        LogString("Changed '" + key + "' from " + prevValue + " to " + GlobalSearchPool.ThreadCount);
                                    }
                                    if (opt.Name == nameof(Hash))
                                    {
                                        GlobalSearchPool.TTable.Initialize(SearchOptions.Hash);
                                    }

                                    LogString("Changed '" + key + "' from " + prevValue + " to " + opt.FieldHandle.GetValue(null));
                                }
                                else
                                {
                                    LogString("[ERROR]: '" + key + "' needs a value between [" + opt.MinValue + ", " + opt.MaxValue + "], " +
                                        "and '" + optValue + "' isn't in that range!");
                                    return;
                                }
                            }
                            else
                            {
                                LogString("[ERROR]: setoption commands for integers need to have a numerical value, " +
                                    "and '" + optValue + "' isn't!");
                                return;
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        LogString("[ERROR]: Failed handling setoption command for '" + optName + "' -> " + optValue);
                        LogString(e.ToString());
                    }

                    return;
                }
            }

            LogString("[WARN]: Got setoption for '" + optName + "' but that isn't an option!");
        }

        public static void ProcessUCIOptions()
        {
            Options = new Dictionary<string, UCIOption>();

            //  Get all "public static" fields, and specifically exclude constant fields (which have field.IsLiteral == true)
            List<FieldInfo> fields = typeof(SearchOptions).GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => !x.IsLiteral).ToList();

            foreach (FieldInfo field in fields)
            {
                string fieldName = field.Name;

                //  Most options are numbers and are called "spin"
                //  If they are true/false, they are called "check"
                string fieldType = field.FieldType == typeof(bool) ? "check" : "spin";
                string defaultValue = field.GetValue(null).ToString().ToLower();

                UCIOption opt = new UCIOption(fieldName, fieldType, defaultValue, field);

                Options.Add(fieldName, opt);
            }

            //SetSPSAOutputParams();

            Options[nameof(Threads)].SetMinMax(1, 512);
            Options[nameof(MultiPV)].SetMinMax(1, 256);
            Options[nameof(Hash)].SetMinMax(1, 1048576);

            Options[nameof(SEMinDepth)].AutoMinMax();
            Options[nameof(SENumerator)].AutoMinMax();
            Options[nameof(SEBeta)].AutoMinMax();
            Options[nameof(SEDepthAdj)].SetMinMax(-3, 2);

            Options[nameof(NMPMinDepth)].AutoMinMax();
            Options[nameof(NMPBaseRed)].AutoMinMax();
            Options[nameof(NMPDepthDiv)].AutoMinMax();
            Options[nameof(NMPEvalDiv)].AutoMinMax();
            Options[nameof(NMPEvalMin)].SetMinMax(0, 6);

            Options[nameof(RFPMaxDepth)].AutoMinMax();
            Options[nameof(RFPMargin)].AutoMinMax();

            Options[nameof(ProbcutBeta)].AutoMinMax();
            Options[nameof(ProbcutBetaImp)].AutoMinMax();
            Options[nameof(ProbcutMinDepth)].SetMinMax(1, 5);

            Options[nameof(ShallowSEEMargin)].AutoMinMax();
            Options[nameof(ShallowMaxDepth)].AutoMinMax();

            Options[nameof(LMRQuietDiv)].AutoMinMax();
            Options[nameof(LMRCaptureDiv)].AutoMinMax();
            Options[nameof(LMRExtMargin)].AutoMinMax();

            Options[nameof(QSFutileMargin)].AutoMinMax();
            Options[nameof(QSSeeMargin)].AutoMinMax();

            Options[nameof(OrderingCheckBonus)].AutoMinMax();
            Options[nameof(OrderingVictimMult)].AutoMinMax();

            Options[nameof(IIRMinDepth)].SetMinMax(2, 6);
            Options[nameof(AspWindow)].AutoMinMax();
            Options[nameof(CaptureBonusMargin)].AutoMinMax();

            Options[nameof(StatBonusMult)].AutoMinMax();
            Options[nameof(StatBonusSub)].AutoMinMax();
            Options[nameof(StatBonusMax)].AutoMinMax();

            Options[nameof(StatMalusMult)].AutoMinMax();
            Options[nameof(StatMalusSub)].AutoMinMax();
            Options[nameof(StatMalusMax)].AutoMinMax();

            Options[nameof(SEEValue_Pawn)].AutoMinMax();
            Options[nameof(SEEValue_Knight)].AutoMinMax();
            Options[nameof(SEEValue_Bishop)].AutoMinMax();
            Options[nameof(SEEValue_Rook)].AutoMinMax();
            Options[nameof(SEEValue_Queen)].AutoMinMax();

            Options[nameof(ValuePawn)].AutoMinMax();
            Options[nameof(ValueKnight)].AutoMinMax();
            Options[nameof(ValueBishop)].AutoMinMax();
            Options[nameof(ValueRook)].AutoMinMax();
            Options[nameof(ValueQueen)].AutoMinMax();


            foreach (var optName in Options.Keys)
            {
                var opt = Options[optName];
                if (opt.FieldHandle.FieldType != typeof(int))
                {
                    continue;
                }

                //  Ensure values are within [Min, Max] and Max > Min
                int currValue = int.Parse(opt.DefaultValue);
                if (currValue < opt.MinValue || currValue > opt.MaxValue || opt.MaxValue < opt.MinValue)
                {
                    Log($"Option '{optName}' has an invalid range! -> [{opt.MinValue} <= {opt.DefaultValue} <= {opt.MaxValue}]!");
                }
            }
        }

        public static void PrintSPSAParams()
        {
            foreach (var optName in Options.Keys)
            {
                if (optName == nameof(SearchOptions.Threads) || optName == nameof(SearchOptions.MultiPV) || optName == nameof(SearchOptions.Hash) || optName == nameof(SearchOptions.UCI_Chess960))
                {
                    continue;
                }

                var opt = Options[optName];
                SendString(opt.GetSPSAFormat());
            }
        }

        private static void SetSPSAOutputParams()
        {
            string output =
                "" +
                "SingularExtensionsMinDepth, 5\r\nSingularExtensionsNumerator, 11\r\nSingularExtensionsBeta, 25\r\nSingularExtensionsDepthAugment, -1\r\nNMPMinDepth, 6\r\nNMPReductionBase, 4\r\nNMPReductionDivisor, 4\r\nNMPEvalDivisor, 184\r\nNMPEvalMin, 2\r\nRFPMaxDepth, 6\r\nRFPMargin, 46\r\nProbCutBeta, 246\r\nProbCutBetaImproving, 103\r\nLMRExtensionThreshold, 132\r\nLMRExchangeBase, 208\r\nHistoryReductionMultiplier, 12\r\nFutilityExchangeBase, 184\r\nExtraCutNodeReductionMinDepth, 4\r\nSkipQuietsMaxDepth, 9\r\nQSSeeThreshold, 78\r\nAspirationWindowMargin, 12\r\nHistoryCaptureBonusMargin, 163\r\nOrderingGivesCheckBonus, 9652\r\nOrderingVictimValueMultiplier, 15\r\nStatBonusMult, 182\r\nStatBonusSub, 80\r\nStatBonusMax, 1653\r\nStatMalusMult, 582\r\nStatMalusSub, 110\r\nStatMalusMax, 1625\r\nSEEValue_Pawn, 101\r\nSEEValue_Knight, 881\r\nSEEValue_Bishop, 1016\r\nSEEValue_Rook, 1351\r\nSEEValue_Queen, 2285\r\nValuePawn, 164\r\nValueKnight, 799\r\nValueBishop, 985\r\nValueRook, 1631\r\nValueQueen, 3000" +
                "";

            var lines = output.Split("\r\n");

            foreach (string line in lines)
            {
                var splits = line.Split(", ");
                Options[splits[0]].DefaultValue = splits[1];
                Options[splits[0]].RefreshBackingField();
            }
        }
    }
}
