using UnityEngine;


namespace ParadoxNotion
{

    ///<summary> Has some prety common operations amongst values.</summary>
    public static class OperationTools
    {

        public static string GetOperationString(AssignOp op) {

            if ( op == AssignOp.Set )
                return " = ";
            if ( op == AssignOp.Add )
                return " += ";
            if ( op == AssignOp.Subtract )
                return " -= ";
            if ( op == AssignOp.Multiply )
                return " *= ";
            if ( op == AssignOp.Divide )
                return " /= ";
            return string.Empty;
        }

        public static float Operate(float a, float b, AssignOp op, float delta = 1f) {
            if ( op == AssignOp.Set )
                return b * delta;
            if ( op == AssignOp.Add )
                return a + ( b * delta );
            if ( op == AssignOp.Subtract )
                return a - ( b * delta );
            if ( op == AssignOp.Multiply )
                return a * ( b * delta );
            if ( op == AssignOp.Divide )
                return a / ( b * delta );
            return a;
        }

        public static int Operate(int a, int b, AssignOp op) {
            if ( op == AssignOp.Set )
                return b;
            if ( op == AssignOp.Add )
                return a + b;
            if ( op == AssignOp.Subtract )
                return a - b;
            if ( op == AssignOp.Multiply )
                return a * b;
            if ( op == AssignOp.Divide )
                return a / b;
            return a;
        }


        public static Vector3 Operate(Vector3 a, Vector3 b, AssignOp op, float delta = 1f) {
            if ( op == AssignOp.Set )
                return b * delta;
            if ( op == AssignOp.Add )
                return a + ( b * delta );
            if ( op == AssignOp.Subtract )
                return a - ( b * delta );
            if ( op == AssignOp.Multiply )
                return Vector3.Scale(a, ( b * delta ));
            if ( op == AssignOp.Divide ) {
                b *= delta;
                return new Vector3(( a ).x / ( b ).x, ( a ).y / ( b ).y, ( a ).z / ( b ).z);
            }
            return a;
        }

        public static string GetCompareString(CompareOp op) {

            if ( op == CompareOp.EqualTo )
                return " == ";
            if ( op == CompareOp.GreaterThan )
                return " > ";
            if ( op == CompareOp.LessThan )
                return " < ";
            if ( op == CompareOp.GreaterOrEqualTo )
                return " >= ";
            if ( op == CompareOp.LessOrEqualTo )
                return " <= ";
            return string.Empty;
        }

        public static bool Compare(float a, float b, CompareOp op, float floatingPoint) {
            if ( op == CompareOp.EqualTo )
                return Mathf.Abs(a - b) <= floatingPoint;
            if ( op == CompareOp.GreaterThan )
                return a > b;
            if ( op == CompareOp.LessThan )
                return a < b;
            if ( op == CompareOp.GreaterOrEqualTo )
                return a >= b;
            if ( op == CompareOp.LessOrEqualTo )
                return a <= b;
            return true;
        }

        public static bool Compare(int a, int b, CompareOp op) {
            if ( op == CompareOp.EqualTo )
                return a == b;
            if ( op == CompareOp.GreaterThan )
                return a > b;
            if ( op == CompareOp.LessThan )
                return a < b;
            if ( op == CompareOp.GreaterOrEqualTo )
                return a >= b;
            if ( op == CompareOp.LessOrEqualTo )
                return a <= b;
            return true;
        }
    }
}