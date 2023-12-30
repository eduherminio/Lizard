﻿namespace LTChess.Logic.Search
{
    public static class SearchOptions
    {
        /// <summary>
        /// This number of threads will be used during searches.
        /// <para></para>
        /// For values above 1, the engine will create extra threads to increase the amount of nodes that can be looked at.
        /// Do note that a decent amount of the nodes that are looked at by secondary threads won't influence anything,
        /// but higher numbers of threads tends to correlate with better playing strength.
        /// </summary>
        public static int Threads = 1;


        /// <summary>
        /// Calls to search will display this amount of principal variation lines. 
        /// <para></para>
        /// Ordinarily engines only search for the 1 "best" move, but with MultiPV values 
        /// above 1 this will also display the 2nd best move, the 3rd best, etc.
        /// </summary>
        public static int MultiPV = 1;



        /// <summary>
        /// The size in megabytes of the transposition table.
        /// </summary>
        public static int Hash = 32;



        /// <summary>
        /// For HalfKA, the NNUE evaluation has a 2 parts: PSQT and Positional. It is usually beneficial to give Positional slightly more weight.
        /// <para></para>
        /// The PSQT component comes from the FeatureTransformer and almost exclusively uses the values of pieces, 
        /// plus a small amount for the square that piece is on.
        /// <para></para>
        /// The Positional component comes from the network layers (AffineTransform, ClippedReLU, ...) and gives a score based on
        /// the relative positions of each piece on the board.
        /// </summary>
        public const bool FavorPositionalEval = true;



        /// <summary>
        /// Whether or not to extend searches if there is only one good move in a position.
        /// <para></para>
        /// This does reduce search speed (in terms of nodes/second), but the accuracy of the additional nodes 
        /// that are searched make this well worth the speed hit.
        /// </summary>
        public const bool UseSingularExtensions = true;

        /// <summary>
        /// The depth must be greater than or equal to this for singular extensions to be considered.
        /// </summary>
        public static int SingularExtensionsMinDepth = 5;



        /// <summary>
        /// Whether or not to prune sequences of moves that don't improve the position evaluation enough 
        /// even when if we give our opponent a free move.
        /// </summary>
        public const bool UseNullMovePruning = true;

        /// <summary>
        /// Nodes need to be at this depth of higher to be considered for pruning.
        /// This also influences the reduced depth that the following nodes are searched
        /// at, which is calculated by adding this flat amount to a node's depth divided by this amount.
        /// i.e. R = <see cref="NullMovePruningMinDepth"/> + (depth / <see cref="NullMovePruningMinDepth"/>)
        /// </summary>
        public static int NullMovePruningMinDepth = 3;



        /// <summary>
        /// Whether or not to ignore moves that don't improve the position evaluation enough.
        /// <br></br>
        /// This is very aggressive in reducing the node count of searches, and generally looks at 2-4x fewer nodes than without RFP.
        /// <para></para>
        /// However, this can cause it to miss some moves (particularly "waiting" moves) until the depth is high enough.
        /// <br></br>For example: 
        /// <see href="https://lichess.org/analysis/fromPosition/n1N3br/2p1Bpkr/1pP2R1b/pP3Pp1/P5P1/1P1p4/p2P4/K7_w_-_-_0_1">this position</see> 
        /// is a mate in 2, but RFP will miss it until depth 8 (regardless of ReverseFutilityPruningMaxDepth).
        /// <br></br>
        /// This is counteracted by the fact that RFP searches are still significantly faster than otherwise, 
        /// so speed-wise this isn't a huge issue.
        /// </summary>
        public const bool UseReverseFutilityPruning = true;
        
        /// <summary>
        /// The depth must be less than or equal to this for reverse futility pruning to be considered.
        /// </summary>
        public static int ReverseFutilityPruningMaxDepth = 8;

        /// <summary>
        /// This amount is added to reverse futility pruning's margin per depth.
        /// </summary>
        public static int ReverseFutilityPruningPerDepth = 65;

        /// <summary>
        /// This amount is removed from the reverse futility pruning margin if the side to move is improving.
        /// </summary>
        private static int ReverseFutilityPruningImproving = 55;


        /// <summary>
        /// Whether or not to try a quiescence search rather than a normal one if the evaluation is extremely bad.
        /// <para></para>
        /// This can prevent moves which just straight up hang a piece from being searched at full depth.
        /// </summary>
        private const bool UseRazoring = false;

        /// <summary>
        /// The depth must be less than or equal to this for razoring to be considered.
        /// </summary>
        private static int RazoringMaxDepth = 6;

        /// <summary>
        /// This amount is added to razoring's margin per depth.
        /// </summary>
        private static int RazoringMargin = 275;

        public const bool UseProbCut = false;
        public static int ProbCutBeta = 175;
        public static int ProbCutBetaImproving = 100;
        public static int ProbCutMinDepth = 5;



        /// <summary>
        /// If moves exceed this margin, they are treated as "good" in multiple places.
        /// </summary>
        public static int ExchangeBase = 200;



        /// <summary>
        /// Cut nodes without transposition table entries will have a reduction applied to them if the search depth is at or above this.
        /// </summary>
        public static int ExtraCutNodeReductionMinDepth = 6;


        
        /// <summary>
        /// If the evaluation at the next depth is within this margin from the previous evaluation,
        /// we use the next depth's evaluation as the starting point for our Alpha/Beta values.
        /// <para></para>
        /// Smaller margins will eliminate more nodes from the search (saves time), but if the margin is too small
        /// we will be forced to redo the search which can waste more time than it saves at high depths.
        /// </summary>
        public static int AspirationWindowMargin = 10;
    }
}
