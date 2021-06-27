using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class HelperMethods
{
	//ClampAngle & WrapAngle courtesy of Parziphal: https://stackoverflow.com/questions/25818897/problems-limiting-object-rotation-with-mathf-clamp
	public static float ClampAngle(float currentValue, float minAngle, float maxAngle, float clampAroundAngle = 0)
	{
		return Mathf.Clamp(WrapAngle(currentValue - (clampAroundAngle + 180)) - 180, minAngle, maxAngle) + 360 + clampAroundAngle;
	}

	public static float WrapAngle(float angle)
	{
		while (angle < 0)
		{
			angle += 360;
		}
		return Mathf.Repeat(angle, 360);
	}
}
