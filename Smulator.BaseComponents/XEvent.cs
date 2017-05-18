using System;
using System.Collections;

namespace Smulator.BaseComponents
{
	public class XEvent
	{
        public ulong FireTime;
        public XObject TargetXObject;
        public object Parameters;
        public int Type;
        public XEvent NextEvent;
        public bool Removed = false;

        public XEvent(ulong fireTime, XObject targetXObject, object parameters, int type)
		{
			this.FireTime = fireTime;
			this.TargetXObject = targetXObject;
			this.Parameters = parameters;
			this.Type = type;
            this.NextEvent = null;
		}
	}
}
