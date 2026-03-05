using UnityEngine;
using System;

public class GJKClosest : MonoBehaviour
{
    //based on https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkComplex.jai
    //gets distance of objects and closest point on both (+ normals)
    //could do it along wishdir if solved in 2d (points projected onto a plane perpendicular to the direction)
    //then make solvetetrahedron always step towards -dir, if it encapsulates origin, we started inside, otherwise find the point closest to origin along -dir to find contact
    //if encapsulates origin then started inside and make tetrahedron step in the wishdir direction, to hopefully find first exit point
    //this is likely one of the following:
    // https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkDir.jai
    // https://github.com/kujukuju/KodaPhysics/blob/master/src/GjkBullet.jai
    //also, can use EPA algorithm to resolve being stuck in a mesh, but might just nudge the player around, that is how source works 
    //might not be needed at all

    class ComplexSimplex {  //could just be a matrix
        Vector3 a; Vector3 a1; Vector3 a2;
        Vector3 b; Vector3 b1; Vector3 b2;
        Vector3 c; Vector3 c1; Vector3 c2;
        Vector3 d; Vector3 d1; Vector3 d2;
        int count;

        Vector3[] a_all = new Vector3[3];
        Vector3[] b_all = new Vector3[3];
        Vector3[] c_all = new Vector3[3];
        Vector3[] d_all = new Vector3[3];

        ComplexSimplex Copy()
        {
            ComplexSimplex ret = new ComplexSimplex();
            ret.a = a; ret.a1 = a1; ret.a2 = a2;
            ret.b = b; ret.b1 = b1; ret.b2 = b2;
            ret.c = c; ret.c1 = c1; ret.c2 = c2;
            ret.d = d; ret.d1 = d1; ret.d2 = d2;
            ret.count = count;
            Array.Copy(a_all, ret.a_all, 3);
            Array.Copy(b_all, ret.b_all, 3);
            Array.Copy(c_all, ret.c_all, 3);
            Array.Copy(d_all, ret.d_all, 3);
            return ret;
        }
    }
}
