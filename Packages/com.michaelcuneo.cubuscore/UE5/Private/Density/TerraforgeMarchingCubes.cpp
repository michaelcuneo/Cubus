#include "Density/TerraforgeMarchingCubes.h"

#include "Core/TerraforgeVoxelMath.h"
#include "Core/TerraforgeVoxelTypes.h"

namespace TerraforgeMarchingCubesLocal
{
    static constexpr int32 CubeCornerOffset[8][3] =
    {
        {0, 0, 0},
        {1, 0, 0},
        {1, 1, 0},
        {0, 1, 0},
        {0, 0, 1},
        {1, 0, 1},
        {1, 1, 1},
        {0, 1, 1}
    };

    static constexpr int32 EdgeConnection[12][2] =
    {
        {0, 1},
        {1, 2},
        {2, 3},
        {3, 0},
        {4, 5},
        {5, 6},
        {6, 7},
        {7, 4},
        {0, 4},
        {1, 5},
        {2, 6},
        {3, 7}
    };

    // Maps each of the 12 cube edges to {axis, dSX, dSY, dSZ}.
    // The "lower" sample of the edge is at (CX+dSX, CY+dSY, CZ+dSZ).
    // axis: 0=+X  1=+Y  2=+Z
    // FlatKey = SampleIndex(CX+dSX, CY+dSY, CZ+dSZ) * 3 + axis
    static constexpr int32 EdgeFlatTable[12][4] =
    {
        {0, 0, 0, 0},  // Edge  0: +X from (CX,   CY,   CZ  )
        {1, 1, 0, 0},  // Edge  1: +Y from (CX+1, CY,   CZ  )
        {0, 0, 1, 0},  // Edge  2: +X from (CX,   CY+1, CZ  )
        {1, 0, 0, 0},  // Edge  3: +Y from (CX,   CY,   CZ  )
        {0, 0, 0, 1},  // Edge  4: +X from (CX,   CY,   CZ+1)
        {1, 1, 0, 1},  // Edge  5: +Y from (CX+1, CY,   CZ+1)
        {0, 0, 1, 1},  // Edge  6: +X from (CX,   CY+1, CZ+1)
        {1, 0, 0, 1},  // Edge  7: +Y from (CX,   CY,   CZ+1)
        {2, 0, 0, 0},  // Edge  8: +Z from (CX,   CY,   CZ  )
        {2, 1, 0, 0},  // Edge  9: +Z from (CX+1, CY,   CZ  )
        {2, 1, 1, 0},  // Edge 10: +Z from (CX+1, CY+1, CZ  )
        {2, 0, 1, 0},  // Edge 11: +Z from (CX,   CY+1, CZ  )
    };

    static constexpr int32 TriangleTable[256][16] =
    {
        { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  1,  9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  8,  3,  9,  8,  1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3,  1,  2, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  2, 10,  0,  2,  9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  8,  3,  2, 10,  8, 10,  9,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  3, 11,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0, 11,  2,  8, 11,  0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  9,  0,  2,  3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1, 11,  2,  1,  9, 11,  9,  8, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  3, 10,  1, 11, 10,  3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0, 10,  1,  0,  8, 10,  8, 11, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  9,  0,  3, 11,  9, 11, 10,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  8, 10, 10,  8, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  7,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  3,  0,  7,  3,  4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  1,  9,  8,  4,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  1,  9,  4,  7,  1,  7,  3,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10,  8,  4,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  4,  7,  3,  0,  4,  1,  2, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  2, 10,  9,  0,  2,  8,  4,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  2, 10,  9,  2,  9,  7,  2,  7,  3,  7,  9,  4, -1, -1, -1, -1 },
        {  8,  4,  7,  3, 11,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  4,  7, 11,  2,  4,  2,  0,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  0,  1,  8,  4,  7,  2,  3, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  7, 11,  9,  4, 11,  9, 11,  2,  9,  2,  1, -1, -1, -1, -1 },
        {  3, 10,  1,  3, 11, 10,  7,  8,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  1, 11, 10,  1,  4, 11,  1,  0,  4,  7, 11,  4, -1, -1, -1, -1 },
        {  4,  7,  8,  9,  0, 11,  9, 11, 10, 11,  0,  3, -1, -1, -1, -1 },
        {  4,  7, 11,  4, 11,  9,  9, 11, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  5,  4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  5,  4,  0,  8,  3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  5,  4,  1,  5,  0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  5,  4,  8,  3,  5,  3,  1,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10,  9,  5,  4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  0,  8,  1,  2, 10,  4,  9,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  5,  2, 10,  5,  4,  2,  4,  0,  2, -1, -1, -1, -1, -1, -1, -1 },
        {  2, 10,  5,  3,  2,  5,  3,  5,  4,  3,  4,  8, -1, -1, -1, -1 },
        {  9,  5,  4,  2,  3, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0, 11,  2,  0,  8, 11,  4,  9,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  5,  4,  0,  1,  5,  2,  3, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  1,  5,  2,  5,  8,  2,  8, 11,  4,  8,  5, -1, -1, -1, -1 },
        { 10,  3, 11, 10,  1,  3,  9,  5,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  9,  5,  0,  8,  1,  8, 10,  1,  8, 11, 10, -1, -1, -1, -1 },
        {  5,  4,  0,  5,  0, 11,  5, 11, 10, 11,  0,  3, -1, -1, -1, -1 },
        {  5,  4,  8,  5,  8, 10, 10,  8, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  7,  8,  5,  7,  9, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  3,  0,  9,  5,  3,  5,  7,  3, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  7,  8,  0,  1,  7,  1,  5,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  5,  3,  3,  5,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  7,  8,  9,  5,  7, 10,  1,  2, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  1,  2,  9,  5,  0,  5,  3,  0,  5,  7,  3, -1, -1, -1, -1 },
        {  8,  0,  2,  8,  2,  5,  8,  5,  7, 10,  5,  2, -1, -1, -1, -1 },
        {  2, 10,  5,  2,  5,  3,  3,  5,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  7,  9,  5,  7,  8,  9,  3, 11,  2, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  5,  7,  9,  7,  2,  9,  2,  0,  2,  7, 11, -1, -1, -1, -1 },
        {  2,  3, 11,  0,  1,  8,  1,  7,  8,  1,  5,  7, -1, -1, -1, -1 },
        { 11,  2,  1, 11,  1,  7,  7,  1,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  5,  8,  8,  5,  7, 10,  1,  3, 10,  3, 11, -1, -1, -1, -1 },
        {  5,  7,  0,  5,  0,  9,  7, 11,  0,  1,  0, 10, 11, 10,  0, -1 },
        { 11, 10,  0, 11,  0,  3, 10,  5,  0,  8,  0,  7,  5,  7,  0, -1 },
        { 11, 10,  5,  7, 11,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  6,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3,  5, 10,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  0,  1,  5, 10,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  8,  3,  1,  9,  8,  5, 10,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  6,  5,  2,  6,  1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  6,  5,  1,  2,  6,  3,  0,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  6,  5,  9,  0,  6,  0,  2,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  5,  9,  8,  5,  8,  2,  5,  2,  6,  3,  2,  8, -1, -1, -1, -1 },
        {  2,  3, 11, 10,  6,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  0,  8, 11,  2,  0, 10,  6,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  1,  9,  2,  3, 11,  5, 10,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  5, 10,  6,  1,  9,  2,  9, 11,  2,  9,  8, 11, -1, -1, -1, -1 },
        {  6,  3, 11,  6,  5,  3,  5,  1,  3, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8, 11,  0, 11,  5,  0,  5,  1,  5, 11,  6, -1, -1, -1, -1 },
        {  3, 11,  6,  0,  3,  6,  0,  6,  5,  0,  5,  9, -1, -1, -1, -1 },
        {  6,  5,  9,  6,  9, 11, 11,  9,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  5, 10,  6,  4,  7,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  3,  0,  4,  7,  3,  6,  5, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  9,  0,  5, 10,  6,  8,  4,  7, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  6,  5,  1,  9,  7,  1,  7,  3,  7,  9,  4, -1, -1, -1, -1 },
        {  6,  1,  2,  6,  5,  1,  4,  7,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2,  5,  5,  2,  6,  3,  0,  4,  3,  4,  7, -1, -1, -1, -1 },
        {  8,  4,  7,  9,  0,  5,  0,  6,  5,  0,  2,  6, -1, -1, -1, -1 },
        {  7,  3,  9,  7,  9,  4,  3,  2,  9,  5,  9,  6,  2,  6,  9, -1 },
        {  3, 11,  2,  7,  8,  4, 10,  6,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  5, 10,  6,  4,  7,  2,  4,  2,  0,  2,  7, 11, -1, -1, -1, -1 },
        {  0,  1,  9,  4,  7,  8,  2,  3, 11,  5, 10,  6, -1, -1, -1, -1 },
        {  9,  2,  1,  9, 11,  2,  9,  4, 11,  7, 11,  4,  5, 10,  6, -1 },
        {  8,  4,  7,  3, 11,  5,  3,  5,  1,  5, 11,  6, -1, -1, -1, -1 },
        {  5,  1, 11,  5, 11,  6,  1,  0, 11,  7, 11,  4,  0,  4, 11, -1 },
        {  0,  5,  9,  0,  6,  5,  0,  3,  6, 11,  6,  3,  8,  4,  7, -1 },
        {  6,  5,  9,  6,  9, 11,  4,  7,  9,  7, 11,  9, -1, -1, -1, -1 },
        { 10,  4,  9,  6,  4, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4, 10,  6,  4,  9, 10,  0,  8,  3, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  0,  1, 10,  6,  0,  6,  4,  0, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  3,  1,  8,  1,  6,  8,  6,  4,  6,  1, 10, -1, -1, -1, -1 },
        {  1,  4,  9,  1,  2,  4,  2,  6,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  0,  8,  1,  2,  9,  2,  4,  9,  2,  6,  4, -1, -1, -1, -1 },
        {  0,  2,  4,  4,  2,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  3,  2,  8,  2,  4,  4,  2,  6, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  4,  9, 10,  6,  4, 11,  2,  3, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  2,  2,  8, 11,  4,  9, 10,  4, 10,  6, -1, -1, -1, -1 },
        {  3, 11,  2,  0,  1,  6,  0,  6,  4,  6,  1, 10, -1, -1, -1, -1 },
        {  6,  4,  1,  6,  1, 10,  4,  8,  1,  2,  1, 11,  8, 11,  1, -1 },
        {  9,  6,  4,  9,  3,  6,  9,  1,  3, 11,  6,  3, -1, -1, -1, -1 },
        {  8, 11,  1,  8,  1,  0, 11,  6,  1,  9,  1,  4,  6,  4,  1, -1 },
        {  3, 11,  6,  3,  6,  0,  0,  6,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  6,  4,  8, 11,  6,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  7, 10,  6,  7,  8, 10,  8,  9, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  7,  3,  0, 10,  7,  0,  9, 10,  6,  7, 10, -1, -1, -1, -1 },
        { 10,  6,  7,  1, 10,  7,  1,  7,  8,  1,  8,  0, -1, -1, -1, -1 },
        { 10,  6,  7, 10,  7,  1,  1,  7,  3, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2,  6,  1,  6,  8,  1,  8,  9,  8,  6,  7, -1, -1, -1, -1 },
        {  2,  6,  9,  2,  9,  1,  6,  7,  9,  0,  9,  3,  7,  3,  9, -1 },
        {  7,  8,  0,  7,  0,  6,  6,  0,  2, -1, -1, -1, -1, -1, -1, -1 },
        {  7,  3,  2,  6,  7,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  3, 11, 10,  6,  8, 10,  8,  9,  8,  6,  7, -1, -1, -1, -1 },
        {  2,  0,  7,  2,  7, 11,  0,  9,  7,  6,  7, 10,  9, 10,  7, -1 },
        {  1,  8,  0,  1,  7,  8,  1, 10,  7,  6,  7, 10,  2,  3, 11, -1 },
        { 11,  2,  1, 11,  1,  7, 10,  6,  1,  6,  7,  1, -1, -1, -1, -1 },
        {  8,  9,  6,  8,  6,  7,  9,  1,  6, 11,  6,  3,  1,  3,  6, -1 },
        {  0,  9,  1, 11,  6,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  7,  8,  0,  7,  0,  6,  3, 11,  0, 11,  6,  0, -1, -1, -1, -1 },
        {  7, 11,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  7,  6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  0,  8, 11,  7,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  1,  9, 11,  7,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  1,  9,  8,  3,  1, 11,  7,  6, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  1,  2,  6, 11,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10,  3,  0,  8,  6, 11,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  9,  0,  2, 10,  9,  6, 11,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  6, 11,  7,  2, 10,  3, 10,  8,  3, 10,  9,  8, -1, -1, -1, -1 },
        {  7,  2,  3,  6,  2,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  7,  0,  8,  7,  6,  0,  6,  2,  0, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  7,  6,  2,  3,  7,  0,  1,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  6,  2,  1,  8,  6,  1,  9,  8,  8,  7,  6, -1, -1, -1, -1 },
        { 10,  7,  6, 10,  1,  7,  1,  3,  7, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  7,  6,  1,  7, 10,  1,  8,  7,  1,  0,  8, -1, -1, -1, -1 },
        {  0,  3,  7,  0,  7, 10,  0, 10,  9,  6, 10,  7, -1, -1, -1, -1 },
        {  7,  6, 10,  7, 10,  8,  8, 10,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  6,  8,  4, 11,  8,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  6, 11,  3,  0,  6,  0,  4,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  6, 11,  8,  4,  6,  9,  0,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  4,  6,  9,  6,  3,  9,  3,  1, 11,  3,  6, -1, -1, -1, -1 },
        {  6,  8,  4,  6, 11,  8,  2, 10,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10,  3,  0, 11,  0,  6, 11,  0,  4,  6, -1, -1, -1, -1 },
        {  4, 11,  8,  4,  6, 11,  0,  2,  9,  2, 10,  9, -1, -1, -1, -1 },
        { 10,  9,  3, 10,  3,  2,  9,  4,  3, 11,  3,  6,  4,  6,  3, -1 },
        {  8,  2,  3,  8,  4,  2,  4,  6,  2, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  4,  2,  4,  6,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  9,  0,  2,  3,  4,  2,  4,  6,  4,  3,  8, -1, -1, -1, -1 },
        {  1,  9,  4,  1,  4,  2,  2,  4,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  1,  3,  8,  6,  1,  8,  4,  6,  6, 10,  1, -1, -1, -1, -1 },
        { 10,  1,  0, 10,  0,  6,  6,  0,  4, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  6,  3,  4,  3,  8,  6, 10,  3,  0,  3,  9, 10,  9,  3, -1 },
        { 10,  9,  4,  6, 10,  4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  9,  5,  7,  6, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3,  4,  9,  5, 11,  7,  6, -1, -1, -1, -1, -1, -1, -1 },
        {  5,  0,  1,  5,  4,  0,  7,  6, 11, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  7,  6,  8,  3,  4,  3,  5,  4,  3,  1,  5, -1, -1, -1, -1 },
        {  9,  5,  4, 10,  1,  2,  7,  6, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  6, 11,  7,  1,  2, 10,  0,  8,  3,  4,  9,  5, -1, -1, -1, -1 },
        {  7,  6, 11,  5,  4, 10,  4,  2, 10,  4,  0,  2, -1, -1, -1, -1 },
        {  3,  4,  8,  3,  5,  4,  3,  2,  5, 10,  5,  2, 11,  7,  6, -1 },
        {  7,  2,  3,  7,  6,  2,  5,  4,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  5,  4,  0,  8,  6,  0,  6,  2,  6,  8,  7, -1, -1, -1, -1 },
        {  3,  6,  2,  3,  7,  6,  1,  5,  0,  5,  4,  0, -1, -1, -1, -1 },
        {  6,  2,  8,  6,  8,  7,  2,  1,  8,  4,  8,  5,  1,  5,  8, -1 },
        {  9,  5,  4, 10,  1,  6,  1,  7,  6,  1,  3,  7, -1, -1, -1, -1 },
        {  1,  6, 10,  1,  7,  6,  1,  0,  7,  8,  7,  0,  9,  5,  4, -1 },
        {  4,  0, 10,  4, 10,  5,  0,  3, 10,  6, 10,  7,  3,  7, 10, -1 },
        {  7,  6, 10,  7, 10,  8,  5,  4, 10,  4,  8, 10, -1, -1, -1, -1 },
        {  6,  9,  5,  6, 11,  9, 11,  8,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  6, 11,  0,  6,  3,  0,  5,  6,  0,  9,  5, -1, -1, -1, -1 },
        {  0, 11,  8,  0,  5, 11,  0,  1,  5,  5,  6, 11, -1, -1, -1, -1 },
        {  6, 11,  3,  6,  3,  5,  5,  3,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 10,  9,  5, 11,  9, 11,  8, 11,  5,  6, -1, -1, -1, -1 },
        {  0, 11,  3,  0,  6, 11,  0,  9,  6,  5,  6,  9,  1,  2, 10, -1 },
        { 11,  8,  5, 11,  5,  6,  8,  0,  5, 10,  5,  2,  0,  2,  5, -1 },
        {  6, 11,  3,  6,  3,  5,  2, 10,  3, 10,  5,  3, -1, -1, -1, -1 },
        {  5,  8,  9,  5,  2,  8,  5,  6,  2,  3,  8,  2, -1, -1, -1, -1 },
        {  9,  5,  6,  9,  6,  0,  0,  6,  2, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  5,  8,  1,  8,  0,  5,  6,  8,  3,  8,  2,  6,  2,  8, -1 },
        {  1,  5,  6,  2,  1,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  3,  6,  1,  6, 10,  3,  8,  6,  5,  6,  9,  8,  9,  6, -1 },
        { 10,  1,  0, 10,  0,  6,  9,  5,  0,  5,  6,  0, -1, -1, -1, -1 },
        {  0,  3,  8,  5,  6, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  5,  6, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  5, 10,  7,  5, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  5, 10, 11,  7,  5,  8,  3,  0, -1, -1, -1, -1, -1, -1, -1 },
        {  5, 11,  7,  5, 10, 11,  1,  9,  0, -1, -1, -1, -1, -1, -1, -1 },
        { 10,  7,  5, 10, 11,  7,  9,  8,  1,  8,  3,  1, -1, -1, -1, -1 },
        { 11,  1,  2, 11,  7,  1,  7,  5,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3,  1,  2,  7,  1,  7,  5,  7,  2, 11, -1, -1, -1, -1 },
        {  9,  7,  5,  9,  2,  7,  9,  0,  2,  2, 11,  7, -1, -1, -1, -1 },
        {  7,  5,  2,  7,  2, 11,  5,  9,  2,  3,  2,  8,  9,  8,  2, -1 },
        {  2,  5, 10,  2,  3,  5,  3,  7,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  2,  0,  8,  5,  2,  8,  7,  5, 10,  2,  5, -1, -1, -1, -1 },
        {  9,  0,  1,  5, 10,  3,  5,  3,  7,  3, 10,  2, -1, -1, -1, -1 },
        {  9,  8,  2,  9,  2,  1,  8,  7,  2, 10,  2,  5,  7,  5,  2, -1 },
        {  1,  3,  5,  3,  7,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  7,  0,  7,  1,  1,  7,  5, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  0,  3,  9,  3,  5,  5,  3,  7, -1, -1, -1, -1, -1, -1, -1 },
        {  9,  8,  7,  5,  9,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  5,  8,  4,  5, 10,  8, 10, 11,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  5,  0,  4,  5, 11,  0,  5, 10, 11, 11,  3,  0, -1, -1, -1, -1 },
        {  0,  1,  9,  8,  4, 10,  8, 10, 11, 10,  4,  5, -1, -1, -1, -1 },
        { 10, 11,  4, 10,  4,  5, 11,  3,  4,  9,  4,  1,  3,  1,  4, -1 },
        {  2,  5,  1,  2,  8,  5,  2, 11,  8,  4,  5,  8, -1, -1, -1, -1 },
        {  0,  4, 11,  0, 11,  3,  4,  5, 11,  2, 11,  1,  5,  1, 11, -1 },
        {  0,  2,  5,  0,  5,  9,  2, 11,  5,  4,  5,  8, 11,  8,  5, -1 },
        {  9,  4,  5,  2, 11,  3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  5, 10,  3,  5,  2,  3,  4,  5,  3,  8,  4, -1, -1, -1, -1 },
        {  5, 10,  2,  5,  2,  4,  4,  2,  0, -1, -1, -1, -1, -1, -1, -1 },
        {  3, 10,  2,  3,  5, 10,  3,  8,  5,  4,  5,  8,  0,  1,  9, -1 },
        {  5, 10,  2,  5,  2,  4,  1,  9,  2,  9,  4,  2, -1, -1, -1, -1 },
        {  8,  4,  5,  8,  5,  3,  3,  5,  1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  4,  5,  1,  0,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  8,  4,  5,  8,  5,  3,  9,  0,  5,  0,  3,  5, -1, -1, -1, -1 },
        {  9,  4,  5, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4, 11,  7,  4,  9, 11,  9, 10, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  8,  3,  4,  9,  7,  9, 11,  7,  9, 10, 11, -1, -1, -1, -1 },
        {  1, 10, 11,  1, 11,  4,  1,  4,  0,  7,  4, 11, -1, -1, -1, -1 },
        {  3,  1,  4,  3,  4,  8,  1, 10,  4,  7,  4, 11, 10, 11,  4, -1 },
        {  4, 11,  7,  9, 11,  4,  9,  2, 11,  9,  1,  2, -1, -1, -1, -1 },
        {  9,  7,  4,  9, 11,  7,  9,  1, 11,  2, 11,  1,  0,  8,  3, -1 },
        { 11,  7,  4, 11,  4,  2,  2,  4,  0, -1, -1, -1, -1, -1, -1, -1 },
        { 11,  7,  4, 11,  4,  2,  8,  3,  4,  3,  2,  4, -1, -1, -1, -1 },
        {  2,  9, 10,  2,  7,  9,  2,  3,  7,  7,  4,  9, -1, -1, -1, -1 },
        {  9, 10,  7,  9,  7,  4, 10,  2,  7,  8,  7,  0,  2,  0,  7, -1 },
        {  3,  7, 10,  3, 10,  2,  7,  4, 10,  1, 10,  0,  4,  0, 10, -1 },
        {  1, 10,  2,  8,  7,  4, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  9,  1,  4,  1,  7,  7,  1,  3, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  9,  1,  4,  1,  7,  0,  8,  1,  8,  7,  1, -1, -1, -1, -1 },
        {  4,  0,  3,  7,  4,  3, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  4,  8,  7, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  9, 10,  8, 10, 11,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  0,  9,  3,  9, 11, 11,  9, 10, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  1, 10,  0, 10,  8,  8, 10, 11, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  1, 10, 11,  3, 10, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  2, 11,  1, 11,  9,  9, 11,  8, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  0,  9,  3,  9, 11,  1,  2,  9,  2, 11,  9, -1, -1, -1, -1 },
        {  0,  2, 11,  8,  0, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  3,  2, 11, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  3,  8,  2,  8, 10, 10,  8,  9, -1, -1, -1, -1, -1, -1, -1 },
        {  9, 10,  2,  0,  9,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  2,  3,  8,  2,  8, 10,  0,  1,  8,  1, 10,  8, -1, -1, -1, -1 },
        {  1, 10,  2, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  1,  3,  8,  9,  1,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  9,  1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        {  0,  3,  8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 },
        { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 }
    };
}

FVector FTerraforgeMarchingCubes::InterpolateVertex(
    const FVector& P0,
    const FVector& P1,
    const float D0,
    const float D1
)
{
    const float Denominator = D0 - D1;

    if (FMath::Abs(Denominator) < KINDA_SMALL_NUMBER)
    {
        return (P0 + P1) * 0.5;
    }

    const float T = FMath::Clamp(D0 / Denominator, 0.0f, 1.0f);

    return P0 + (P1 - P0) * static_cast<double>(T);
}

void FTerraforgeMarchingCubes::GenerateMesh(
    const FIntVector& ChunkCoord,
    FDensityLookup DensityLookup,
    FMaterialLookup MaterialLookup,
    FTerraforgeVoxelMeshData& OutMesh,
    const int32 CellStep,
    const bool bSmoothNormalsAcrossMaterialBoundaries
)
{
    OutMesh.Reset();

    constexpr int32 Size = TerraforgeVoxel::ChunkSize;
    const double S = TerraforgeVoxel::VoxelSize;

    const int32 SafeCellStep =
        FMath::Clamp(CellStep, 1, TerraforgeVoxel::ChunkSize);

    const int32 NumCellsAxis =
        FMath::DivideAndRoundUp(Size, SafeCellStep);

    const int32 NumSamplesAxis =
        NumCellsAxis + 1;

    const int32 TotalCells =
        NumCellsAxis * NumCellsAxis * NumCellsAxis;

    auto LocalToWorldVoxel =
        [ChunkCoord](const int32 X, const int32 Y, const int32 Z) -> FIntVector
        {
            return FIntVector(
                ChunkCoord.X * TerraforgeVoxel::ChunkSize + X,
                ChunkCoord.Y * TerraforgeVoxel::ChunkSize + Y,
                ChunkCoord.Z * TerraforgeVoxel::ChunkSize + Z
            );
        };

    const FVector ChunkWorldOrigin(
        static_cast<double>(ChunkCoord.X * TerraforgeVoxel::ChunkSize) * S,
        static_cast<double>(ChunkCoord.Y * TerraforgeVoxel::ChunkSize) * S,
        static_cast<double>(ChunkCoord.Z * TerraforgeVoxel::ChunkSize) * S
    );

    auto SampleIndex =
        [NumSamplesAxis](const int32 SX, const int32 SY, const int32 SZ) -> int32
        {
            return SX + NumSamplesAxis * (SY + NumSamplesAxis * SZ);
        };

    const int32 TotalSamples =
        NumSamplesAxis * NumSamplesAxis * NumSamplesAxis;

    TArray<float> DensityGrid;
    DensityGrid.SetNumUninitialized(TotalSamples);

    TArray<FVector> NormalGrid;
    NormalGrid.SetNumUninitialized(TotalSamples);

    TArray<int32> CachedMaterialGrid;
    CachedMaterialGrid.Init(-1, TotalSamples);

    for (int32 SZ = 0; SZ < NumSamplesAxis; ++SZ)
    {
        for (int32 SY = 0; SY < NumSamplesAxis; ++SY)
        {
            for (int32 SX = 0; SX < NumSamplesAxis; ++SX)
            {
                const int32 LX = SX * SafeCellStep;
                const int32 LY = SY * SafeCellStep;
                const int32 LZ = SZ * SafeCellStep;

                const FIntVector WorldVoxelCoord =
                    LocalToWorldVoxel(LX, LY, LZ);

                DensityGrid[SampleIndex(SX, SY, SZ)] =
                    DensityLookup(WorldVoxelCoord);
            }
        }
    }

    for (int32 SZ = 0; SZ < NumSamplesAxis; ++SZ)
    {
        for (int32 SY = 0; SY < NumSamplesAxis; ++SY)
        {
            for (int32 SX = 0; SX < NumSamplesAxis; ++SX)
            {
                const int32 LX = SX * SafeCellStep;
                const int32 LY = SY * SafeCellStep;
                const int32 LZ = SZ * SafeCellStep;

                const FIntVector WorldVoxelCoord =
                    LocalToWorldVoxel(LX, LY, LZ);

                const auto SampleDensityAtWorldOffset =
                    [&](const FIntVector& Offset) -> float
                    {
                        return DensityLookup(WorldVoxelCoord + Offset);
                    };

                const float DX =
                    SampleDensityAtWorldOffset(FIntVector(-SafeCellStep, 0, 0)) -
                    SampleDensityAtWorldOffset(FIntVector(SafeCellStep, 0, 0));

                const float DY =
                    SampleDensityAtWorldOffset(FIntVector(0, -SafeCellStep, 0)) -
                    SampleDensityAtWorldOffset(FIntVector(0, SafeCellStep, 0));

                const float DZ =
                    SampleDensityAtWorldOffset(FIntVector(0, 0, -SafeCellStep)) -
                    SampleDensityAtWorldOffset(FIntVector(0, 0, SafeCellStep));

                FVector Normal(DX, DY, DZ);

                if (Normal.IsNearlyZero())
                {
                    Normal = FVector::UpVector;
                }
                else
                {
                    Normal.Normalize();
                }

                NormalGrid[SampleIndex(SX, SY, SZ)] = Normal;
            }
        }
    }

    auto InterpolatedNormal =
        [](const FVector& N0, const FVector& N1, const float D0, const float D1) -> FVector
        {
            const float Denominator = D0 - D1;

            if (FMath::Abs(Denominator) < KINDA_SMALL_NUMBER)
            {
                return (N0 + N1).GetSafeNormal();
            }

            const float T =
                FMath::Clamp(D0 / Denominator, 0.0f, 1.0f);

            return (N0 + (N1 - N0) * static_cast<double>(T)).GetSafeNormal();
        };

    auto ChooseMaterial =
        [&CachedMaterialGrid, &MaterialLookup](
            const FIntVector* WorldCorners,
            const float* Densities,
            const int32* CornerSampleIndices
            ) -> uint16
        {
            uint16 BestMaterialId = 0;
            float BestDensity = -FLT_MAX;

            for (int32 Corner = 0; Corner < 8; ++Corner)
            {
                if (Densities[Corner] <= 0.0f)
                {
                    continue;
                }

                if (Densities[Corner] <= BestDensity)
                {
                    continue;
                }

                const int32 SampleIdx =
                    CornerSampleIndices[Corner];

                int32& CachedMaterial =
                    CachedMaterialGrid[SampleIdx];

                if (CachedMaterial < 0)
                {
                    CachedMaterial =
                        static_cast<int32>(MaterialLookup(WorldCorners[Corner]));
                }

                const uint16 CandidateMaterialId =
                    static_cast<uint16>(FMath::Max(0, CachedMaterial));

                if (CandidateMaterialId == 0)
                {
                    continue;
                }

                BestDensity = Densities[Corner];
                BestMaterialId = CandidateMaterialId;
            }

            return BestMaterialId != 0 ? BestMaterialId : 1;
        };

    auto ProjectUV = [](const FVector& P, const FVector& N) -> FVector2D
        {
            constexpr double UVScale = 0.0025;

            const FVector AbsNormal(
                FMath::Abs(N.X),
                FMath::Abs(N.Y),
                FMath::Abs(N.Z)
            );

            if (AbsNormal.Z >= AbsNormal.X && AbsNormal.Z >= AbsNormal.Y)
            {
                return FVector2D(P.X * UVScale, P.Y * UVScale);
            }

            if (AbsNormal.X >= AbsNormal.Y)
            {
                return FVector2D(P.Y * UVScale, P.Z * UVScale);
            }

            return FVector2D(P.X * UVScale, P.Z * UVScale);
        };

    // Flat edge-vertex cache: index = SampleIndex(lowerSX,lowerSY,lowerSZ)*3 + axis.
    // Only allocated and used in smooth mode; non-smooth generates independent
    // vertices per cell (correct for hard material boundaries).
    TArray<int32> FlatEdgeCache;
    if (bSmoothNormalsAcrossMaterialBoundaries)
    {
        FlatEdgeCache.Init(INDEX_NONE, TotalSamples * 3);
    }

    // --- Pass 1: Cheap CubeIndex scan — compact active (non-trivial) cells ---
    // Each cell is just 8 DensityGrid reads + 8 comparisons. Fully solid (255)
    // and fully empty (0) cells are skipped; only surface-crossing cells are
    // kept. For typical terrain, 60-80% of cells are trivial and never enter
    // the expensive Pass 2 path.
    struct FActiveCell { uint8 CX, CY, CZ, CubeIndex; };
    static_assert(TerraforgeVoxel::ChunkSize <= 255, "FActiveCell stores compact uint8 coordinates.");
    TArray<FActiveCell> ActiveCells;
    ActiveCells.Reserve(FMath::Max(64, TotalCells / 4));

    for (int32 PCZ = 0; PCZ < NumCellsAxis; ++PCZ)
    {
        for (int32 PCY = 0; PCY < NumCellsAxis; ++PCY)
        {
            for (int32 PCX = 0; PCX < NumCellsAxis; ++PCX)
            {
                int32 CubeIndex = 0;

                for (int32 Corner = 0; Corner < 8; ++Corner)
                {
                    const int32 SX =
                        PCX + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][0];
                    const int32 SY =
                        PCY + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][1];
                    const int32 SZ =
                        PCZ + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][2];

                    if (DensityGrid[SampleIndex(SX, SY, SZ)] > 0.0f)
                    {
                        CubeIndex |= 1 << Corner;
                    }
                }

                if (CubeIndex != 0 && CubeIndex != 255)
                {
                    const int32* Edges =
                        TerraforgeMarchingCubesLocal::TriangleTable[CubeIndex];

                    if (Edges[0] >= 0)
                    {
                        ActiveCells.Add(
                            { (uint8)PCX, (uint8)PCY, (uint8)PCZ, (uint8)CubeIndex }
                        );
                    }
                }
            }
        }
    }

    // Precise reserve now that we know the exact active cell count.
    // ~5 triangles / active cell on average → 9 verts + 15 indices per cell.
    {
        const int32 N = ActiveCells.Num();
        OutMesh.Reserve(N * 9, N * 15);
    }

    // --- Pass 2: Full mesh generation — only over active cells ---
    for (const FActiveCell& Cell : ActiveCells)
    {
        const int32 CX = static_cast<int32>(Cell.CX);
        const int32 CY = static_cast<int32>(Cell.CY);
        const int32 CZ = static_cast<int32>(Cell.CZ);
        const int32 CubeIndex = static_cast<int32>(Cell.CubeIndex);

        const int32* TriangleEdges =
            TerraforgeMarchingCubesLocal::TriangleTable[CubeIndex];

        FVector Positions[8];
        FVector CornerNormals[8];
        FIntVector WorldCorners[8];
        float Densities[8];
        int32 CornerSampleIndices[8];

        for (int32 Corner = 0; Corner < 8; ++Corner)
        {
            const int32 SX =
                CX + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][0];
            const int32 SY =
                CY + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][1];
            const int32 SZ =
                CZ + TerraforgeMarchingCubesLocal::CubeCornerOffset[Corner][2];

            const int32 LX = SX * SafeCellStep;
            const int32 LY = SY * SafeCellStep;
            const int32 LZ = SZ * SafeCellStep;

            const int32 SampleIdx = SampleIndex(SX, SY, SZ);

            CornerSampleIndices[Corner] = SampleIdx;

            Positions[Corner] = FVector(
                static_cast<double>(LX) * S,
                static_cast<double>(LY) * S,
                static_cast<double>(LZ) * S
            );

            WorldCorners[Corner] = LocalToWorldVoxel(LX, LY, LZ);

            Densities[Corner] = DensityGrid[SampleIdx];
            CornerNormals[Corner] = NormalGrid[SampleIdx];
        }

        FVector EdgeVertices[12];
        FVector EdgeNormals[12];
        int32 EdgeVertexIndices[12] = { INDEX_NONE };
        bool bHasEdgeVertex[12] = { false };

        const uint16 MaterialId =
            ChooseMaterial(
                WorldCorners,
                Densities,
                CornerSampleIndices
            );

        const FColor VertexColor =
            bSmoothNormalsAcrossMaterialBoundaries
            ? FColor::White
            : FColor(
                static_cast<uint8>(
                    FMath::Clamp<int32>(
                        120 + static_cast<int32>(MaterialId) * 20,
                        80,
                        220
                    )
                ),
                static_cast<uint8>(
                    FMath::Clamp<int32>(
                        120 + static_cast<int32>(MaterialId) * 20,
                        80,
                        220
                    )
                ),
                static_cast<uint8>(
                    FMath::Clamp<int32>(
                        120 + static_cast<int32>(MaterialId) * 20,
                        80,
                        220
                    )
                ),
                255
            );
        const FProcMeshTangent Tangent(1.0f, 0.0f, 0.0f);

        auto BuildEdge = [&](const int32 EdgeIndex) -> int32
        {
            if (bHasEdgeVertex[EdgeIndex])
            {
                return EdgeVertexIndices[EdgeIndex];
            }

            const int32 CornerA =
                TerraforgeMarchingCubesLocal::EdgeConnection[EdgeIndex][0];
            const int32 CornerB =
                TerraforgeMarchingCubesLocal::EdgeConnection[EdgeIndex][1];

            EdgeVertices[EdgeIndex] =
                FTerraforgeMarchingCubes::InterpolateVertex(
                    Positions[CornerA],
                    Positions[CornerB],
                    Densities[CornerA],
                    Densities[CornerB]
                );

            EdgeNormals[EdgeIndex] =
                InterpolatedNormal(
                    CornerNormals[CornerA],
                    CornerNormals[CornerB],
                    Densities[CornerA],
                    Densities[CornerB]
                );

            FVector FinalNormal = EdgeNormals[EdgeIndex].GetSafeNormal();

            if (FinalNormal.IsNearlyZero())
            {
                FinalNormal = FVector::UpVector;
            }

            if (bSmoothNormalsAcrossMaterialBoundaries)
            {
                const int32* ET =
                    TerraforgeMarchingCubesLocal::EdgeFlatTable[EdgeIndex];

                const int32 FlatKey =
                    SampleIndex(CX + ET[1], CY + ET[2], CZ + ET[3]) * 3 + ET[0];

                int32& CachedIdx = FlatEdgeCache[FlatKey];

                if (CachedIdx != INDEX_NONE)
                {
                    EdgeVertexIndices[EdgeIndex] = CachedIdx;
                }
                else
                {
                    const int32 NewIndex = OutMesh.Vertices.Num();

                    OutMesh.Vertices.Add(EdgeVertices[EdgeIndex]);
                    OutMesh.Normals.Add(FinalNormal);
                    OutMesh.UVs.Add(ProjectUV(ChunkWorldOrigin + EdgeVertices[EdgeIndex], FinalNormal));
                    OutMesh.VertexColors.Add(VertexColor);
                    OutMesh.Tangents.Add(Tangent);

                    EdgeVertexIndices[EdgeIndex] = NewIndex;
                    CachedIdx = NewIndex;
                }
            }
            else
            {
                const int32 NewIndex = OutMesh.Vertices.Num();

                OutMesh.Vertices.Add(EdgeVertices[EdgeIndex]);
                OutMesh.Normals.Add(FinalNormal);
                            OutMesh.UVs.Add(ProjectUV(ChunkWorldOrigin + EdgeVertices[EdgeIndex], FinalNormal));
                OutMesh.VertexColors.Add(VertexColor);
                OutMesh.Tangents.Add(Tangent);

                EdgeVertexIndices[EdgeIndex] = NewIndex;
            }

            bHasEdgeVertex[EdgeIndex] = true;
            return EdgeVertexIndices[EdgeIndex];
        };

        for (int32 TriangleIndex = 0; TriangleIndex < 16; TriangleIndex += 3)
        {
            const int32 Edge0 = TriangleEdges[TriangleIndex + 0];

            if (Edge0 < 0)
            {
                break;
            }

            const int32 Edge1 = TriangleEdges[TriangleIndex + 1];
            const int32 Edge2 = TriangleEdges[TriangleIndex + 2];

            if (
                Edge1 < 0 ||
                Edge2 < 0 ||
                Edge0 >= 12 ||
                Edge1 >= 12 ||
                Edge2 >= 12
                )
            {
                break;
            }

            const int32 V0 = BuildEdge(Edge0);
            const int32 V1 = BuildEdge(Edge1);
            const int32 V2 = BuildEdge(Edge2);

            if (
                V0 == INDEX_NONE ||
                V1 == INDEX_NONE ||
                V2 == INDEX_NONE ||
                V0 == V1 ||
                V1 == V2 ||
                V0 == V2
                )
            {
                continue;
            }

            const FVector RawFaceNormal =
                FVector::CrossProduct(
                    OutMesh.Vertices[V1] - OutMesh.Vertices[V0],
                    OutMesh.Vertices[V2] - OutMesh.Vertices[V0]
                );

            if (RawFaceNormal.SizeSquared() < KINDA_SMALL_NUMBER)
            {
                continue;
            }

            OutMesh.Triangles.Add(V0);
            OutMesh.Triangles.Add(V1);
            OutMesh.Triangles.Add(V2);
        }
    }
}
