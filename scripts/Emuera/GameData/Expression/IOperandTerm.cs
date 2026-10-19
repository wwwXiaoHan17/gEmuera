using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Expression
{
	internal abstract class IOperandTerm
	{
		public IOperandTerm(Type t)
        {
            type = EraTypeHelper.FromClrType(t);
        }
		public IOperandTerm(EraType t)
		{
			type = t;
		}
		public Type GetOperandType()
        {
            return EraTypeHelper.ToClrType(type);
        }

		public virtual EraType GetEraType()
		{
			return type;
		}

        public virtual Int64 GetIntValue(ExpressionMediator exm)
        {
            return 0;
        }
        public virtual string GetStrValue(ExpressionMediator exm)
        {
            return "";
        }
        public virtual SingleTerm GetValue(ExpressionMediator exm)
        {
            if (type == EraType.Integer)
                return new SingleTerm(0);
            else if (type == EraType.Float)
                return new SingleTerm(0.0);
            else
                return new SingleTerm("");
        }
        public bool IsInteger
        {
            get { return type == EraType.Integer; }
        }
        public bool IsString
        {
            get { return type == EraType.String; }
        }
        public bool IsFloat
        {
            get { return type == EraType.Float; }
        }
        readonly EraType type;

        public virtual double GetFloatValue(ExpressionMediator exm)
        {
            return GetIntValue(exm);
        }

		/// <summary>
		/// 定数を解体して可能ならSingleTerm化する
		/// defineの都合上、2回以上呼ばれる可能性がある
		/// </summary>
        public virtual IOperandTerm Restructure(ExpressionMediator exm)
        {
			return this;
        }
	}
}
