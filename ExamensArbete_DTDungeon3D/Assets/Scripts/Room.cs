using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Del av examensarbete för kandidatexamen vid Högskolan i Skövde med inriktning dataspelsutveckling, år 2021. Skapat av Emil Birgersson
/// </summary>

[System.Serializable]
public class Room
{
	public Transform RoomObject { get { return roomObject; } }
	private Transform roomObject;

	public Transform[] ChildTiles { get { return childTiles; } }
	private Transform[] childTiles;

	public BoxCollider[] ChildColliders { get { return childColliders; } }
	private BoxCollider[] childColliders;

	public MeshRenderer[] ChildRenderers { get { return childRenderers; } }
	private MeshRenderer[] childRenderers;

	public int SizeX { get { return sizeX; } }
	public int SizeZ { get { return sizeZ; } }
	private int sizeX, sizeZ;

	public BoxCollider RoomCollider { get { return roomCollider; } }
	private BoxCollider roomCollider;

	public int RoomIndex { get { return roomIndex; } }
	private int roomIndex;

	public Room(Transform roomObject, List<Transform> childTiles, int X, int Z, BoxCollider roomCollider, int roomIndex)
	{
		this.roomObject = roomObject;
		this.childTiles = new Transform[childTiles.Count];
		this.childColliders = new BoxCollider[childTiles.Count];
		this.childRenderers = new MeshRenderer[childTiles.Count];
		for (int i = 0; i < this.childTiles.Length; i++)
		{
			this.childTiles[i] = childTiles[i];
			this.childColliders[i] = childTiles[i].GetComponent<BoxCollider>();
			this.childRenderers[i] = childTiles[i].GetComponent<MeshRenderer>();
		}

		this.sizeX = X;
		this.sizeZ = Z;
		this.roomCollider = roomCollider;
		this.roomIndex = roomIndex;
	}
}