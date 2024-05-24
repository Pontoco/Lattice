using System.Collections;
using System.Collections.Generic;
using GTF;
using Lattice.StandardLibrary;
using UnityEngine;

namespace Samples.CrossReferences
{
    [LatticeNodes]
    public static class CrossReferences
    {
        public static int TestState(this ref int state)
        {
            return state;
        }

        public static void IncreaseState(ref int state)
        {
            state += 1;
        }
    }
}