using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// Del av examensarbete för kandidatexamen vid Högskolan i Skövde med inriktning dataspelsutveckling, år 2021. Skapat av Emil Birgersson
/// </summary>

public class OverlapFixingAgent : MonoBehaviour
{
	public bool IsOverlapping { get { return isOverlapping; } }
	private bool isOverlapping = true;

	private BoxCollider boxCollider;

	void Awake()
	{
		boxCollider = GetComponent<BoxCollider>();
		// for (int i = 0; i < transform.childCount; i++)
		// {
		// 	transform.GetChild(i).GetComponent<BoxCollider>().enabled = false;
		// }
	}

	void FixedUpdate()
	{
		Collider[] collisions = Physics.OverlapBox(transform.position + boxCollider.center, boxCollider.size / 2, Quaternion.identity, LayerMask.GetMask("Room"));
		isOverlapping = false;
		if (collisions.Length > 0)
		{
			isOverlapping = true;
			int neighbours = 0;
			Vector3 moveAdjustVector = new Vector3();
			for (int j = 0; j < collisions.Length; j++)
			{
				if (collisions[j].transform == transform) continue;
				moveAdjustVector += (transform.position) - (collisions[j].transform.position);
				neighbours++;
			}
			if (neighbours > 0)
			{
				moveAdjustVector /= neighbours;
				moveAdjustVector.Normalize();
				transform.position += moveAdjustVector * Time.fixedDeltaTime;
				CeilFloorPosition(transform);
			}
		}

		//* Switched to doing active ceil-flooring between collision checks in order to make sure ceil-flooring doesn't screw up placement at the end
		//* Re-introduced ceil-flooring at the end to make sure all tile-rooms are ceiled or floored to integer-position
		CeilFloorPosition(transform);
	}

	void CeilFloorPosition(Transform t)
	{
		t.position = new Vector3()
		{
			x = t.position.x <= 0.5f ? Mathf.FloorToInt(t.position.x) : Mathf.CeilToInt(t.position.x),
			y = t.position.y <= 0.5f ? Mathf.FloorToInt(t.position.y) : Mathf.CeilToInt(t.position.y),
			z = t.position.z <= 0.5f ? Mathf.FloorToInt(t.position.z) : Mathf.CeilToInt(t.position.z)
		};
	}
}
