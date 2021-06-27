using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using zenzyde;

/// <summary>
/// Examensarbete för kandidatexamen vid Högskolan i Skövde med inriktning dataspelsutveckling, år 2021. Skapat av Emil Birgersson
/// </summary>

public class DTDungeon3D : MonoBehaviour
{
	//[SerializeField] Transform[] availableFloorTiles, availableFrontWallTiles, availableBackWallTiles, availableLeftWallTiles, availableRightWallTiles, availableRoofTiles;
	[SerializeField] Transform[] availableRoomTiles;
	[SerializeField] Transform corridorTile, stairTile;
	[SerializeField] float maxRoomSpawningRange;
	[SerializeField] int availableRoomsToSpawn;
	int pathfindingStairCost = 2, pathfindingCorridorCost = 1, pathfindingRoomCost = 3, pathfindingBaseCost = 100;
	[SerializeField] Vector3Int maxCreatedRoomSize, minCreatedRoomSize, minimumAcceptableRoomSize;
	[Range(0f, 1f)] float percentOfGraphCyclesToKeep = 0.15f;
	[Range(.2f, .8f)] float chanceForBiggerRooms = 0.2f;
	[SerializeField] bool noCorridorBetweenStartEnd = false, visualizeSpecialRooms = true;
	[SerializeField] Color32 startRoomVisualizationColor, endRoomVisualizationColor, overlappingRoomVisualizationColor;

	private List<Room> createdRooms, acceptedRooms, corridorRooms;
	private DTPoint3D[] dtGraph, mstGraph, residualGraph;
	private List<DTPoint3D> finalGraph;
	private Room startRoom, endRoom;
	private Dictionary<Vector3Int, AStarNode> grid;
	private Vector3Int min, max;

	private bool spreadRooms = false, startedMakingDungeon = false, madeDungeon = false, loggedTests = false;

	//* Declaring rand here instead so that the creation of room-tiles uses the same random generator, helps increasing variety it seems
	System.Random rand = new System.Random();

	void OnValidate()
	{
		//* Safety checks
		if (maxRoomSpawningRange < 0) maxRoomSpawningRange = 0;

		if (minCreatedRoomSize.x > maxCreatedRoomSize.x) minCreatedRoomSize = new Vector3Int(maxCreatedRoomSize.x, minCreatedRoomSize.y, minCreatedRoomSize.z);
		if (minCreatedRoomSize.y > maxCreatedRoomSize.y) minCreatedRoomSize = new Vector3Int(minCreatedRoomSize.x, maxCreatedRoomSize.y, minCreatedRoomSize.z);
		if (minCreatedRoomSize.z > maxCreatedRoomSize.z) minCreatedRoomSize = new Vector3Int(minCreatedRoomSize.x, minCreatedRoomSize.y, maxCreatedRoomSize.z);

		if (minCreatedRoomSize.x <= 0) minCreatedRoomSize = new Vector3Int(1, minCreatedRoomSize.y, minCreatedRoomSize.z);
		if (minCreatedRoomSize.y <= 0) minCreatedRoomSize = new Vector3Int(minCreatedRoomSize.x, 1, minCreatedRoomSize.z);
		if (minCreatedRoomSize.z <= 0) minCreatedRoomSize = new Vector3Int(minCreatedRoomSize.x, minCreatedRoomSize.y, 1);

		if (minimumAcceptableRoomSize.x <= 0) minimumAcceptableRoomSize = new Vector3Int(1, minimumAcceptableRoomSize.y, minimumAcceptableRoomSize.z);
		if (minimumAcceptableRoomSize.y <= 0) minimumAcceptableRoomSize = new Vector3Int(minimumAcceptableRoomSize.x, 1, minimumAcceptableRoomSize.z);
		if (minimumAcceptableRoomSize.z <= 0) minimumAcceptableRoomSize = new Vector3Int(minimumAcceptableRoomSize.x, minimumAcceptableRoomSize.y, 1);

		if (maxCreatedRoomSize.x <= 0) maxCreatedRoomSize = new Vector3Int(1, maxCreatedRoomSize.y, maxCreatedRoomSize.z);
		if (maxCreatedRoomSize.y <= 0) maxCreatedRoomSize = new Vector3Int(maxCreatedRoomSize.x, 1, maxCreatedRoomSize.z);
		if (maxCreatedRoomSize.z <= 0) maxCreatedRoomSize = new Vector3Int(maxCreatedRoomSize.x, maxCreatedRoomSize.y, 1);

		pathfindingCorridorCost = Mathf.Abs(pathfindingCorridorCost);
		pathfindingRoomCost = Mathf.Abs(pathfindingRoomCost);
		pathfindingStairCost = Mathf.Abs(pathfindingStairCost);
		pathfindingBaseCost = Mathf.Abs(pathfindingBaseCost);
	}

	// Start is called before the first frame update
	void Start()
	{
		CreateSpreadRooms();
		StartCoroutine(UntangleCreatedRooms());
	}

	void Update()
	{
		if (!spreadRooms) return;

		if (!startedMakingDungeon)
		{
			startedMakingDungeon = true;
			SelectAcceptableRooms();
			CreateDelaunayTriangulation();
			CreatePrimsMST();
			InsertCycles();
			InsertOverlappingRooms();
			ChooseStartEndRooms();
			CreateGrid();
			StartCoroutine(BuildCorridors2());
		}

		if (!loggedTests && madeDungeon)
		{
			LogTimeTest();
			LogSize();
			LogTotalDensity(grid);
			LogPhysicalDistanceStartToEnd();
			LogRoomHeightDifference();
			LogPercentOfCreatedRoomsAccepted();
			loggedTests = true;
		}
	}

	void CreateSpreadRooms()
	{
		createdRooms = new List<Room>();

		for (int i = 0; i < availableRoomsToSpawn; i++)
		{
			//* No need for multiplying with random value, inside unit sphere already puts a point somewhere inside a sphere,
			//* -- hence the name, instead of only at the circumference
			Vector3 point = transform.position + UnityEngine.Random.insideUnitSphere * maxRoomSpawningRange;
			Room tileRoom = CreateTileRoom(point, i);
			createdRooms.Add(tileRoom);
		}
	}

	//* Creation of tile-rooms shows a bias for creating very oblong room-tiles -- sort of fixed!
	Room CreateTileRoom(Vector3 center, int roomIndex)
	{
		Transform roomTile = new GameObject("RoomTile").transform;
		BoxCollider collider = roomTile.gameObject.AddComponent<BoxCollider>();
		collider.gameObject.layer = LayerMask.NameToLayer("Room");
		Vector3Int meanSize = GetMeanRoomSize();

		//* Very simple emulation of the random distribution used in TinyKeep
		//* -- Added the low chance getting a maximized size, seems to produce much more similar results, only gotta fixa the ratio issue now i think
		int X = rand.NextDouble() < .2f ?
			Mathf.Max(rand.Next(minCreatedRoomSize.x, maxCreatedRoomSize.x), meanSize.x) :
			Mathf.Min(rand.Next(minCreatedRoomSize.x, maxCreatedRoomSize.x), meanSize.x);
		int Z = rand.NextDouble() < .2f ?
			Mathf.Max(rand.Next(minCreatedRoomSize.z, maxCreatedRoomSize.z), meanSize.z) :
			Mathf.Min(rand.Next(minCreatedRoomSize.z, maxCreatedRoomSize.z), meanSize.z);
		int Y = rand.NextDouble() < .2f ?
			Mathf.Max(rand.Next(minCreatedRoomSize.y, maxCreatedRoomSize.y), meanSize.y) :
			Mathf.Min(rand.Next(minCreatedRoomSize.y, maxCreatedRoomSize.y), meanSize.y);

		float xPercentage = GetPercentageRatio(X, Z);
		float zPercentage = GetPercentageRatio(Z, X);

		float xQuota = xPercentage / zPercentage;
		float zQuota = zPercentage / xPercentage;

		//* A more successfull attempt at ensuring a decent ratio between width and depth, not squarey yet no too oblong
		if (xPercentage > zPercentage)
		{
			//* Too oblong on the X-axis
			if (zQuota < 0.23)
			{
				int tempZ = Z;
				while (zQuota < 0.33)
				{
					tempZ++;
					zPercentage = GetPercentageRatio(tempZ, X);
					zQuota = zPercentage / xPercentage;
				}
				Z = tempZ;
			}
		}
		else if (xPercentage == zPercentage)
		{
			//* Too squarey
			bool adjustX = rand.NextDouble() > 0.5f;
			if (adjustX)
			{
				int tempX = X;
				while (xQuota < 0.33)
				{
					tempX++;
					xPercentage = GetPercentageRatio(tempX, Z);
					xQuota = xPercentage / zPercentage;
				}
				X = tempX;
			}
			else
			{
				int tempZ = Z;
				while (zQuota < 0.33)
				{
					tempZ++;
					zPercentage = GetPercentageRatio(tempZ, X);
					zQuota = zPercentage / xPercentage;
				}
				Z = tempZ;
			}
		}
		else
		{
			//* Too oblong on the Z-axis
			if (xQuota < 0.23)
			{
				int tempX = X;
				while (xQuota < 0.33)
				{
					tempX++;
					xPercentage = GetPercentageRatio(tempX, Z);
					xQuota = xPercentage / zPercentage;
				}
				X = tempX;
			}
		}

		//* Half and substract in order to center positions
		X -= X / 2;
		Z -= Z / 2;

		List<Transform> childTiles = new List<Transform>();

		Transform chosenWallTile = availableRoomTiles[rand.Next(0, availableRoomTiles.Length)];

		for (float x = -X; x < X; x++)
		{
			for (float z = -Z; z < Z; z++)
			{
				for (float y = 0; y < Y; y++)
				{
					//* Walls
					if (chosenWallTile != null)
					{
						if (x > -X && x < X - 1 && z > -Z && z < Z - 1 && y > 0 && y < Y - 1) continue;
						Transform childWallTile = Instantiate(chosenWallTile, new Vector3(x, y, z), Quaternion.identity, roomTile);
						childWallTile.gameObject.layer = LayerMask.NameToLayer("Default");
						childTiles.Add(childWallTile);
					}
				}
			}
		}

		//* Re-center the collider
		float height = Y == 1 ? 0 : Y % 2 == 0 ? (Y / 2f) - 0.5f : Mathf.CeilToInt(Y / 3);
		Vector3 newCenter = collider.center;
		newCenter -= new Vector3(.5f, 0, .5f);
		newCenter.y = height;
		collider.center = newCenter;
		//* Adjust the size to encompass all child-tiles & properly represent the volume of the room
		collider.size = new Vector3Int()
		{
			x = X * 2,
			y = Y,
			z = Z * 2
		};

		roomTile.position = center;

		return new Room(roomTile, childTiles, X, Z, collider, roomIndex);
	}

	//* Calculate mean size for greater chance of less oblong room-tiles
	//* Inspiration from: https://stackoverflow.com/questions/37966950/c-sharp-calculate-mean-of-the-values-in-int-array
	Vector3Int GetMeanRoomSize()
	{
		Vector3Int sum = new Vector3Int();
		Vector3Int[] sizes = new Vector3Int[availableRoomsToSpawn];
		for (int i = 0; i < availableRoomsToSpawn; i++)
		{
			int X = rand.Next(minCreatedRoomSize.x, maxCreatedRoomSize.x);

			int Z = rand.Next(minCreatedRoomSize.z, maxCreatedRoomSize.z);

			int Y = rand.Next(minCreatedRoomSize.y, maxCreatedRoomSize.y);

			sizes[i] = new Vector3Int()
			{
				x = X,
				y = Y,
				z = Z
			};

			sum += sizes[i];
		}

		Vector3Int mean = sum / sizes.Length;
		return mean;
	}

	//* Calculate percentage for ratio
	float GetPercentageRatio(int a, int b)
	{
		return (float)a / (float)b;
	}

	IEnumerator UntangleCreatedRooms()
	{
		List<OverlapFixingAgent> agents = new List<OverlapFixingAgent>();
		foreach (Room room in createdRooms)
		{
			OverlapFixingAgent agent = room.RoomObject.gameObject.AddComponent<OverlapFixingAgent>();
			agents.Add(agent);
		}
		bool agentsNotDone = true;
		while (agentsNotDone)
		{
			for (int i = 0; i < agents.Count; i++)
			{
				if (agents[i].IsOverlapping)
					agentsNotDone = false;
				yield return null;
				if (agents[i].IsOverlapping)
					agentsNotDone = false;
			}
			yield return null;
		}

		foreach (Room room in createdRooms)
		{
			Destroy(room.RoomObject.GetComponent<OverlapFixingAgent>());
			for (int i = 0; i < room.ChildColliders.Length; i++)
			{
				room.ChildColliders[i].enabled = true;
			}
		}

		spreadRooms = true;
	}

	//* Flooring the position makes it easier to construct actual corridors & rooms later on, it'll be a tile-like grid
	//* Flooring or Ceiling based on nearest rounding, to make up for collision check sometimes missing
	Vector3Int CeilFloorPosition(Vector3 position)
	{
		return new Vector3Int()
		{
			x = position.x <= 0.5f ? Mathf.FloorToInt(position.x) : Mathf.CeilToInt(position.x),
			y = position.y <= 0.5f ? Mathf.FloorToInt(position.y) : Mathf.CeilToInt(position.y),
			z = position.z <= 0.5f ? Mathf.FloorToInt(position.z) : Mathf.CeilToInt(position.z)
		};
	}

	void SelectAcceptableRooms()
	{
		acceptedRooms = new List<Room>();
		foreach (Room room in createdRooms)
		{
			int X = (int)room.RoomCollider.size.x;
			int Z = (int)room.RoomCollider.size.z;
			int Y = (int)room.RoomCollider.size.y;
			if (X >= minimumAcceptableRoomSize.x && Z >= minimumAcceptableRoomSize.z && Y >= minimumAcceptableRoomSize.y)
				acceptedRooms.Add(room);
			// else
			// 	room.RoomObject.gameObject.SetActive(false);
		}
	}

	void CreateDelaunayTriangulation()
	{
		DTPoint3D[] dTPoints = new DTPoint3D[acceptedRooms.Count];
		for (int i = 0; i < acceptedRooms.Count; i++)
		{
			dTPoints[i] = new DTPoint3D(Vector3Int.RoundToInt(acceptedRooms[i].RoomObject.position), acceptedRooms[i].RoomIndex, acceptedRooms[i]);
		}
		DelaunayTriangulator3D delaunayTriangulator = new DelaunayTriangulator3D(dTPoints, Vector3Int.RoundToInt(transform.position));
		dtGraph = delaunayTriangulator.Triangulate();

		// foreach (DTPoint3D point in dtGraph)
		// {
		// 	foreach (DTPoint3D neighbour in point.NEIGHBOURS)
		// 	{
		// 		Debug.DrawLine(point.POSITION, neighbour.POSITION, Color.green, 100f);
		// 	}
		// }

		// foreach (DTPoint3D point in dtGraph)
		// {
		// 	foreach (DTEdge3D edge in point.EDGES)
		// 	{
		// 		Debug.DrawLine(edge.START.POSITION, edge.END.POSITION, Color.green, 100f);
		// 	}
		// }
	}

	#region DT-graph visualization
	void TraverseGraph(DTPoint3D[] root)
	{
		HashSet<DTPoint3D> visitedPoints = new HashSet<DTPoint3D>();
		Queue<DTPoint3D> pointsToVisit = new Queue<DTPoint3D>();
		pointsToVisit.Enqueue(root[0]);
		visitedPoints.Add(root[0]);
		while (pointsToVisit.Count > 0)
		{
			DTPoint3D currentPoint = pointsToVisit.Dequeue();
			visitedPoints.Add(currentPoint);
			foreach (DTPoint3D neighbour in currentPoint.NEIGHBOURS)
			{
				if (visitedPoints.Contains(neighbour)) continue;
				pointsToVisit.Enqueue(neighbour);
			}
		}

		//* Don't draw dupes
		HashSet<KeyValuePair<DTPoint3D, DTPoint3D>> edges = new HashSet<KeyValuePair<DTPoint3D, DTPoint3D>>();
		for (int i = 0; i < visitedPoints.Count; i++)
		{
			DTPoint3D point = visitedPoints.ElementAt(i);
			for (int j = 0; j < visitedPoints.Count; j++)
			{
				DTPoint3D otherPoint = visitedPoints.ElementAt(j);
				if (point.POSITION == otherPoint.POSITION && point.INDEX == otherPoint.INDEX) continue;
				if (edges.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(point, otherPoint)) ||
					edges.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(otherPoint, point)))
				{
					continue;
				}
				point.RemoveNeighbour(otherPoint);
				otherPoint.RemoveNeighbour(point);
				KeyValuePair<DTPoint3D, DTPoint3D> pair = new KeyValuePair<DTPoint3D, DTPoint3D>(point, otherPoint);
				edges.Add(pair);
				Debug.DrawLine(point.POSITION, otherPoint.POSITION, Color.green, 10f);
			}
		}
	}
	#endregion

	//* Attempt based on video: https://www.youtube.com/watch?v=cplfcGZmX7I -- didn't work?!....
	//* Trying again with the approach from the video which made CreatePrimsMST work, gonna try to make my own implementation based on it..
	//* Success!!!!!!!!!!!!!
	void CreatePrimsMST()
	{
		HashSet<DTPoint3D> visitedPoints = new HashSet<DTPoint3D>();
		int[] lengths = new int[dtGraph.Length];
		mstGraph = new DTPoint3D[dtGraph.Length];
		residualGraph = new DTPoint3D[dtGraph.Length];

		//* Initialize all lengths except for the first one with a high value, root/first node should be closest
		for (int i = 0; i < dtGraph.Length; i++)
		{
			if (i == 0)
			{
				lengths[i] = 0;
			}
			else
			{
				lengths[i] = int.MaxValue;
			}
		}

		int iterations = 0;
		while (visitedPoints.Count < dtGraph.Length)
		{
			//* Find the node with minimum key/length value
			DTPoint3D current = null;
			int lowestCurrentLength = int.MaxValue;
			for (int i = 0; i < dtGraph.Length; i++)
			{
				if (lengths[i] < lowestCurrentLength && !visitedPoints.Contains(dtGraph[i]))
				{
					lowestCurrentLength = lengths[i];
					current = dtGraph[i];
				}
			}
			//* Lowest node found, add to visitedPoints
			visitedPoints.Add(current);
			//* Also insert into mst graph
			mstGraph[iterations] = new DTPoint3D(current.POSITION, current.INDEX, current.ROOM);

			//* Update all adjacent length values
			DTPoint3D closestNeighbour = null;
			int lowestNeighbourLength = int.MaxValue;
			for (int i = 0; i < current.NEIGHBOURS.Count; i++)
			{
				DTPoint3D neighbour = current.NEIGHBOURS.ElementAt(i);
				int dtGraphIndex = Array.IndexOf(dtGraph, neighbour);
				int length = (int)(neighbour.POSITION - current.POSITION).magnitude;
				//* Update adjacent length value if needed
				if (length < lengths[dtGraphIndex])
				{
					lengths[dtGraphIndex] = length;
				}
				//* Check if not yet visited point in order to avoid adding a back-forth-edge and limit/cut off the algorithm --
				//* as well as checking if it is the current lowest length from the current starting-point
				if (length < lowestNeighbourLength && !visitedPoints.Contains(neighbour))
				{
					lowestNeighbourLength = length;
					closestNeighbour = new DTPoint3D(neighbour.POSITION, neighbour.INDEX, neighbour.ROOM);
				}
				else //* if already visited point or length is not the lowest, add point to the residual graph for use with re-introducing cycles later on
				{
					DTPoint3D residualNeighbour = new DTPoint3D(neighbour.POSITION, neighbour.INDEX, neighbour.ROOM);
					residualGraph[iterations] = residualNeighbour;
					residualNeighbour.AddEdge(mstGraph[iterations]);
					residualNeighbour.AddNeighbour(mstGraph[iterations]);
				}
			}
			//* Add neighour/edge to current & vice versa
			if (closestNeighbour != null)
			{
				mstGraph[iterations].AddEdge(closestNeighbour);
				mstGraph[iterations].AddNeighbour(closestNeighbour);
				closestNeighbour.AddEdge(mstGraph[iterations]);
				closestNeighbour.AddNeighbour(mstGraph[iterations]);
				iterations++;
			}
		}

		// for (int i = 0; i < mstGraph.Length; i++)
		// {
		// 	DTPoint3D point = mstGraph[i];
		// 	if (point == null)
		// 	{
		// 		Debug.Log("Point is null");
		// 		continue;
		// 	}
		// 	for (int j = 0; j < point.NEIGHBOURS.Count; j++)
		// 	{
		// 		DTPoint3D neighbour = point.NEIGHBOURS.ElementAt(j);
		// 		if (neighbour == null)
		// 		{
		// 			Debug.Log("Neighbour is null");
		// 			continue;
		// 		}
		// 		Debug.DrawLine(point.POSITION, neighbour.POSITION, Color.green, 100f);
		// 	}
		// }
		// for (int i = 0; i < residualGraph.Length; i++)
		// {
		// 	DTPoint3D point = residualGraph[i];
		// 	if (point == null)
		// 	{
		// 		Debug.Log("Residual point is null");
		// 		continue;
		// 	}
		// 	for (int j = 0; j < point.NEIGHBOURS.Count; j++)
		// 	{
		// 		DTPoint3D neighbour = point.NEIGHBOURS.ElementAt(j);
		// 		if (neighbour == null)
		// 		{
		// 			Debug.Log("Residual neighbour is null");
		// 			continue;
		// 		}
		// 		Debug.DrawLine(point.POSITION, neighbour.POSITION, Color.red, 100f);
		// 	}
		// }
	}

	//* Think this works now, original design based on the instructions by Phi Dinh on reddit
	void InsertCycles()
	{
		//* Changed from Floor to Ceil to increase the chance of at least one cycle being added
		int cyclesToAdd = Mathf.CeilToInt(residualGraph.Length * percentOfGraphCyclesToKeep);

		finalGraph = new List<DTPoint3D>();//DTPoint3D[dtGraph.Length];

		for (int i = 0; i < mstGraph.Length; i++)
		{
			finalGraph.Add(mstGraph[i]);
		}

		int iteration = 0;
		int cyclesAdded = 0;
		while (cyclesAdded < cyclesToAdd)
		{
			iteration %= finalGraph.Count;
			DTPoint3D currentFinal = finalGraph[iteration];
			for (int i = 0; i < residualGraph.Length; i++)
			{
				if (residualGraph[i] == null) continue;
				if (!currentFinal.NEIGHBOURS.Contains(residualGraph[i]) && residualGraph[i].NEIGHBOURS.Contains(currentFinal) && rand.NextDouble() < 0.125)
				{
					//* Creating a new DTPoint with the same input as the original in order to add a "clean slate" as a neighbour, no back-forth-dependance
					DTPoint3D neighbour = new DTPoint3D(residualGraph[i].POSITION, residualGraph[i].INDEX, residualGraph[i].ROOM);
					currentFinal.AddEdge(neighbour);
					currentFinal.AddNeighbour(neighbour);
					cyclesAdded++;
					break;
				}
			}
			iteration++;
		}

		// for (int i = 0; i < finalGraph.Count; i++)
		// {
		// 	DTPoint3D verificationPoint = residualGraph.Where(x => x.INDEX == finalGraph[i].INDEX).FirstOrDefault();
		// 	if (verificationPoint == null) continue;
		// 	DTPoint3D point = finalGraph[i];
		// 	if (point == null)
		// 	{
		// 		Debug.Log("Final point is null");
		// 		continue;
		// 	}
		// 	for (int j = 0; j < point.NEIGHBOURS.Count; j++)
		// 	{
		// 		DTPoint3D neighbour = point.NEIGHBOURS.ElementAt(j);
		// 		if (neighbour == null)
		// 		{
		// 			Debug.Log("Final neighbour is null");
		// 			continue;
		// 		}
		// 		Debug.DrawLine(point.POSITION, (point.POSITION + neighbour.POSITION) / 2, Color.cyan, 100f);
		// 	}
		// }
	}

	//* I wanna check between all nodes & their neighbours that are already in the graph and neighbour collections --
	//! i don't wanna check all the new nodes that will be added as well
	//? Some rooms end up connecting to themselves, i've got no idea where?!?
	//* Think i found the error, when creating a "newneighbour" i didn't consider the fact that after the for-loop it could refer to the same object because i fetch--
	//* the last object i added to the neighbourlist at the end of the for-loop, so when this happens i simply don't add the neighbour to itself!
	void InsertOverlappingRooms()
	{
		corridorRooms = new List<Room>();

		//* Activate all rooms in order to detect overlap
		foreach (Room room in createdRooms)
		{
			//room.RoomObject.gameObject.SetActive(true);
			if (!acceptedRooms.Contains(room))
			{
				room.RoomCollider.enabled = true;
				foreach (BoxCollider childCol in room.ChildColliders)
				{
					childCol.enabled = false;
				}
			}
			else
			{
				room.RoomCollider.enabled = true;
			}
		}

		//* Move a Physics.OverlapBox from start to end & activate all rooms that aren't activated
		//* Add the new rooms as neighbours to the current room in finalGraph
		int finalCount = finalGraph.Count;
		int acceptedRoomsCount = acceptedRooms.Count;
		for (int k = 0; k < finalCount; k++)
		{
			DTPoint3D room = finalGraph[k];
			int neighbourCount = room.NEIGHBOURS.Count;
			for (int j = 0; j < neighbourCount; j++)
			{
				DTPoint3D neighbour = room.NEIGHBOURS.ElementAt(j);
				Vector3 start = room.POSITION;//.ROOM.RoomObject.position;
				Vector3 end = neighbour.POSITION;//.ROOM.RoomObject.position;

				Vector3 dummyPos = start;//Vector3.Lerp(start, end, 0.5f);
				Vector3 overlapBoxSize = new Vector3(
					Mathf.Max(room.ROOM.RoomCollider.size.x, neighbour.ROOM.RoomCollider.size.x),
					Mathf.Max(room.ROOM.RoomCollider.size.y, neighbour.ROOM.RoomCollider.size.y),
					1);

				HashSet<Collider> colliderSet = new HashSet<Collider>();

				//* Add colliders faster by doing a boxcast & jumping from hit to hit to end
				while (true)
				{
					//* Boxcast from current to end to detect rooms in the way
					if (Physics.BoxCast(dummyPos, overlapBoxSize / 2, (end - dummyPos).normalized, out RaycastHit hit, Quaternion.LookRotation((end - dummyPos).normalized),
						(end - dummyPos).magnitude, LayerMask.GetMask("Room")))
					{
						//* If length between start & end is less than current & start it means current has gone past end, abort!
						if ((end - start).sqrMagnitude < (dummyPos - start).sqrMagnitude)
						{
							break;
						}
						//* Only add colliders of rooms that aren't the start or end or room tiles
						if (hit.collider != room.ROOM.RoomCollider && hit.collider != neighbour.ROOM.RoomCollider &&
							hit.transform.parent == null)
							colliderSet.Add(hit.collider);
						//* If we hit the neighbour it means we reached the end, abort!
						if (hit.collider == neighbour.ROOM.RoomCollider)
							break;
						// Debug.DrawLine(dummyPos, hit.point, Color.magenta, 10f);
						//* Move dummypos to next position & boxcast!
						dummyPos = hit.point - hit.normal;
					}
					//* If nothing was it there's no overlapping rooms to add, abort!
					else
					{
						break;
					}
				}

				if (colliderSet.Count > 0)
				{
					List<DTPoint3D> previousNeighbours = new List<DTPoint3D>();
					DTPoint3D newNeighbour = null;
					DTPoint3D oldNeighbour = null;
					bool addedFirstNeighbour = false;
					for (int i = 0; i < colliderSet.Count; i++)
					{
						//* Skip if collider has a parent, then it's a floorTile & not a room
						if (colliderSet.ElementAt(i).transform.parent != null)
						{
							continue;
						}

						Room currentOverlapRoom = createdRooms.Where(r => r.RoomCollider == colliderSet.ElementAt(i)).FirstOrDefault();

						bool isOldNeighbour = false;
						for (int x = 0; x < room.NEIGHBOURS.Count; x++)
						{
							//* Room already has this neighbour, edit the edges
							if (room.NEIGHBOURS.ElementAt(x).ROOM == currentOverlapRoom)
							{
								isOldNeighbour = true;
								oldNeighbour = room.NEIGHBOURS.ElementAt(x);
								room.RemoveNeighbour(oldNeighbour);
								room.RemoveEdge(oldNeighbour);
								neighbourCount--;
								oldNeighbour.RemoveNeighbour(room);
								oldNeighbour.RemoveEdge(room);
								if (previousNeighbours.Count > 0)
								{
									DTPoint3D previousNeighbour = previousNeighbours[previousNeighbours.Count - 1];

									if (!room.NEIGHBOURS.Contains(previousNeighbour))
									{
										previousNeighbour.AddEdge(oldNeighbour);
										previousNeighbour.AddNeighbour(oldNeighbour);
									}
								}
								previousNeighbours.Add(oldNeighbour);
								newNeighbour = oldNeighbour;
								// Debug.DrawLine(room.POSITION, oldNeighbour.POSITION, Color.black, 10f);
								break;
							}
						}
						if (isOldNeighbour)
							continue;

						//* Check if overlapping room is already an accepted room, in that case it's already in the finalGraph, then just edit it's edges --
						//* don't create a new point
						bool isAlreadyAccepted = false;
						for (int x = 0; x < acceptedRoomsCount; x++)
						{
							if (acceptedRooms[x] == currentOverlapRoom)
							{
								isAlreadyAccepted = true;
								DTPoint3D existingNeighbour = null;
								for (int y = 0; y < finalCount; y++)
								{
									if (finalGraph[y].ROOM == acceptedRooms[x])
										existingNeighbour = finalGraph[y];
								}

								if (existingNeighbour != null)
								{
									if (!addedFirstNeighbour)
									{
										room.AddEdge(existingNeighbour);
										room.AddNeighbour(existingNeighbour);
										room.RemoveEdge(neighbour);
										room.RemoveNeighbour(neighbour);
										addedFirstNeighbour = true;
										Debug.DrawLine(room.POSITION, existingNeighbour.POSITION, (Color.yellow + Color.red) / 2, 10f);
									}
									else
									{
										if (previousNeighbours.Count > 0)
										{
											DTPoint3D previousNeighbour = previousNeighbours[previousNeighbours.Count - 1];
											previousNeighbour.AddEdge(existingNeighbour);
											previousNeighbour.AddNeighbour(existingNeighbour);
											// Debug.DrawLine(previousNeighbour.POSITION, existingNeighbour.POSITION, Color.green, 10f);
										}
									}
									previousNeighbours.Add(existingNeighbour);
									newNeighbour = existingNeighbour;
								}
								break;
							}
						}
						if (isAlreadyAccepted)
							continue;

						//* Create & add new neighbour to room
						newNeighbour = new DTPoint3D(
								(int)currentOverlapRoom.RoomObject.position.x, (int)currentOverlapRoom.RoomObject.position.y, (int)currentOverlapRoom.RoomObject.position.z,
								currentOverlapRoom.RoomIndex,
								currentOverlapRoom
							);

						//* Add newNeighbour to previous collided room, form a link from start room to end room with overlapping neighbours in between
						//? Alt: instead of chains, add the new links as new elements in the finalGraph, makes traversing for adding CorridorBuilders easier later
						if (!addedFirstNeighbour) //* Will still work with slight modification
						{
							room.AddEdge(newNeighbour);
							room.AddNeighbour(newNeighbour);
							room.RemoveEdge(neighbour);
							room.RemoveNeighbour(neighbour);
							addedFirstNeighbour = true;
							// Debug.DrawLine(room.POSITION, newNeighbour.POSITION, (Color.yellow + Color.red) / 2, 10f);
						}
						else
						{
							if (previousNeighbours.Count > 0)
							{
								DTPoint3D previousNeighbour = previousNeighbours[previousNeighbours.Count - 1];
								previousNeighbour.AddEdge(newNeighbour);
								previousNeighbour.AddNeighbour(newNeighbour);
								// Debug.DrawLine(previousNeighbour.POSITION, newNeighbour.POSITION, Color.cyan, 10f);
							}
						}

						previousNeighbours.Add(newNeighbour);
						//* Add newNeighbour to finalGraph to be able to build all corridors properly later
						if (!finalGraph.Contains(newNeighbour))
							finalGraph.Add(newNeighbour);
						//* Add overlapping room to accepted rooms in order to be able to deactivate the proper rooms below
						if (!corridorRooms.Contains(currentOverlapRoom))
							corridorRooms.Add(currentOverlapRoom);
					}

					if (previousNeighbours.Count == 0) continue;
					DTPoint3D lastPreviousNeighbour = previousNeighbours[previousNeighbours.Count - 1];
					//* Self-connection fix!
					if (lastPreviousNeighbour.INDEX == newNeighbour.INDEX)
					{
						newNeighbour.AddEdge(neighbour);
						newNeighbour.AddNeighbour(neighbour);
						// Debug.DrawLine(newNeighbour.POSITION, neighbour.POSITION, Color.red, 10f);
					}
					else
					{
						lastPreviousNeighbour.AddEdge(newNeighbour);
						lastPreviousNeighbour.AddNeighbour(newNeighbour);
						newNeighbour.AddEdge(neighbour);
						newNeighbour.AddNeighbour(neighbour);
						// Debug.DrawLine(lastPreviousNeighbour.POSITION, newNeighbour.POSITION, Color.red, 10f);
						// Debug.DrawLine(newNeighbour.POSITION, neighbour.POSITION, Color.red, 10f);
					}
				}
			}
		}

		//* De-activate/destroy all rooms which aren't overlapping
		for (int i = createdRooms.Count - 1; i >= 0; i--)
		{
			if (corridorRooms.Contains(createdRooms[i]) || acceptedRooms.Contains(createdRooms[i]))
			{
				if (visualizeSpecialRooms && corridorRooms.Contains(createdRooms[i]))
				{
					foreach (MeshRenderer mesh in createdRooms[i].ChildRenderers)
					{
						MaterialPropertyBlock mpb = new MaterialPropertyBlock();
						mpb.SetColor("_BaseColor", overlappingRoomVisualizationColor);
						mesh.SetPropertyBlock(mpb);
					}
				}
				foreach (BoxCollider childCol in createdRooms[i].ChildColliders)
				{
					childCol.enabled = true;
				}
			}
			else
			{
				Destroy(createdRooms[i].RoomObject.gameObject);
				createdRooms.RemoveAt(i);
			}
		}
	}

	void InsertOverlappingRooms2()
	{
		corridorRooms = new List<Room>();

		//* Activate all rooms in order to detect overlap
		foreach (Room room in createdRooms)
		{
			if (!acceptedRooms.Contains(room))
			{
				room.RoomCollider.enabled = true;
				foreach (BoxCollider childCol in room.ChildColliders)
				{
					childCol.enabled = false;
				}
			}
			else
			{
				room.RoomCollider.enabled = true;
			}
		}

		//* Move a Physics.OverlapBox from start to end & activate all rooms that aren't activated
		//* Add the new rooms as neighbours to the current room in finalGraph
		int finalCount = finalGraph.Count;
		int acceptedRoomsCount = acceptedRooms.Count;
		for (int k = 0; k < finalCount; k++)
		{
			DTPoint3D room = finalGraph[k];
			int neighbourCount = room.NEIGHBOURS.Count;
			for (int j = 0; j < neighbourCount; j++)
			{
				DTPoint3D neighbour = room.NEIGHBOURS.ElementAt(j);
				Vector3 start = room.POSITION;//.ROOM.RoomObject.position;
				Vector3 end = neighbour.POSITION;//.ROOM.RoomObject.position;

				Vector3 dummyPos = start;//Vector3.Lerp(start, end, 0.5f);
				Vector3 overlapBoxSize = new Vector3(
					Mathf.Max(room.ROOM.RoomCollider.size.x, neighbour.ROOM.RoomCollider.size.x),
					Mathf.Max(room.ROOM.RoomCollider.size.y, neighbour.ROOM.RoomCollider.size.y),
					1);

				HashSet<Collider> colliderSet = new HashSet<Collider>();

				//* Add colliders faster by doing a boxcast & jumping from hit to hit to end
				while (true)
				{
					//* Boxcast from current to end to detect rooms in the way
					if (Physics.BoxCast(dummyPos, overlapBoxSize / 2, (end - dummyPos).normalized, out RaycastHit hit, Quaternion.LookRotation((end - dummyPos).normalized),
						(end - dummyPos).magnitude, LayerMask.GetMask("Room")))
					{
						//* If length between start & end is less than current & start it means current has gone past end, abort!
						if ((end - start).sqrMagnitude < (dummyPos - start).sqrMagnitude)
						{
							break;
						}
						//* Only add colliders of rooms that aren't the start or end or room tiles
						if (hit.collider != room.ROOM.RoomCollider && hit.collider != neighbour.ROOM.RoomCollider &&
							hit.transform.parent == null)
							colliderSet.Add(hit.collider);
						//* If we hit the neighbour it means we reached the end, abort!
						if (hit.collider == neighbour.ROOM.RoomCollider)
							break;
						//* Move dummypos to next position & boxcast!
						dummyPos = hit.point - hit.normal;
					}
					//* If nothing was it there's no overlapping rooms to add, abort!
					else
					{
						break;
					}
				}

				if (colliderSet.Count > 0)
				{
					for (int i = 0; i < colliderSet.Count; i++)
					{
						//* Skip if collider has a parent, then it's a floorTile & not a room
						if (colliderSet.ElementAt(i).transform.parent != null)
						{
							continue;
						}

						Room currentOverlapRoom = createdRooms.Where(r => r.RoomCollider == colliderSet.ElementAt(i)).FirstOrDefault();

						if (room.NEIGHBOURS.Where(r => r.ROOM == currentOverlapRoom).FirstOrDefault() != null)
						{
							continue;
						}

						bool skip = false;
						for (int x = 0; x < acceptedRoomsCount; x++)
						{
							if (acceptedRooms[x] == currentOverlapRoom)
								skip = true;
						}
						if (skip) continue;

						DTPoint3D newNeighbour = new DTPoint3D(
								(int)currentOverlapRoom.RoomObject.position.x, (int)currentOverlapRoom.RoomObject.position.y, (int)currentOverlapRoom.RoomObject.position.z,
								currentOverlapRoom.RoomIndex,
								currentOverlapRoom
							);

						room.AddNeighbour(newNeighbour);
						room.AddEdge(newNeighbour);

						if (!finalGraph.Contains(newNeighbour))
							finalGraph.Add(newNeighbour);

						if (!corridorRooms.Contains(currentOverlapRoom))
							corridorRooms.Add(currentOverlapRoom);
					}
				}
			}
		}

		//* De-activate all rooms which aren't overlapping
		for (int i = createdRooms.Count - 1; i >= 0; i--)
		{
			if (corridorRooms.Contains(createdRooms[i]) || acceptedRooms.Contains(createdRooms[i]))
			{
				createdRooms[i].RoomCollider.enabled = true;
				foreach (BoxCollider childCol in createdRooms[i].ChildColliders)
				{
					childCol.enabled = true;
				}
			}
			else
			{
				Destroy(createdRooms[i].RoomObject.gameObject);
				createdRooms.RemoveAt(i);
			}
		}
	}

	void CreateGrid()
	{
		//* Build simulated grid
		int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
		for (int i = 0; i < acceptedRooms.Count; i++)
		{
			if (acceptedRooms[i].RoomCollider.bounds.min.x < minX)
				minX = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.min).x;

			if (acceptedRooms[i].RoomCollider.bounds.max.x > maxX)
				maxX = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.max).x;

			if (acceptedRooms[i].RoomCollider.bounds.min.z < minZ)
				minZ = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.min).z;

			if (acceptedRooms[i].RoomCollider.bounds.max.z > maxZ)
				maxZ = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.max).z;

			if (acceptedRooms[i].RoomCollider.bounds.min.y < minY)
				minY = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.min).y;

			if (acceptedRooms[i].RoomCollider.bounds.max.y > maxY)
				maxY = CeilFloorPosition(acceptedRooms[i].RoomCollider.bounds.max).y;
		}

		for (int i = 0; i < corridorRooms.Count; i++)
		{
			if (corridorRooms[i].RoomCollider.bounds.min.x < minX)
				minX = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.min).x;

			if (corridorRooms[i].RoomCollider.bounds.max.x > maxX)
				maxX = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.max).x;

			if (corridorRooms[i].RoomCollider.bounds.min.z < minZ)
				minZ = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.min).z;

			if (corridorRooms[i].RoomCollider.bounds.max.z > maxZ)
				maxZ = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.max).z;

			if (corridorRooms[i].RoomCollider.bounds.min.y < minY)
				minY = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.min).y;

			if (corridorRooms[i].RoomCollider.bounds.max.y > maxY)
				maxY = CeilFloorPosition(corridorRooms[i].RoomCollider.bounds.max).y;
		}

		min = new Vector3Int(minX, minY, minZ);
		max = new Vector3Int(maxX, maxY, maxZ);

		// Debug.DrawRay(min, new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(min, new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(max.x, min.y, min.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(max.x, min.y, min.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(max.x, min.y, max.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(max.x, min.y, max.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(min.x, min.y, max.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(min.x, min.y, max.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(min.x, max.y, max.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(min.x, max.y, max.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(min.x, max.y, min.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(min.x, max.y, min.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(new Vector3(max.x, max.y, min.z), new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(new Vector3(max.x, max.y, min.z), new Vector3(-1, -1, -1), Color.blue, 10f);

		// Debug.DrawRay(max, new Vector3(1, 1, 1), Color.blue, 10f);
		// Debug.DrawRay(max, new Vector3(-1, -1, -1), Color.blue, 10f);

		grid = new Dictionary<Vector3Int, AStarNode>();

		for (int i = minX; i < maxX; i++)
		{
			for (int j = minY; j < maxY; j++)
			{
				for (int k = minZ; k < maxZ; k++)
				{
					Vector3Int position = new Vector3Int(i, j, k);
					grid.Add(position, new AStarNode(position));
					if (Physics.OverlapBox(grid[position].position, Vector3.one / 3f, Quaternion.identity, LayerMask.GetMask("Room")).Length > 0)
						grid[position].isRoom = true;
					else
						grid[position].isEmpty = true;
					// Debug.DrawRay(position, new Vector3(.5f, .5f, .5f), Color.blue, 10f);
				}
			}
		}
	}

	IEnumerator BuildCorridors2()
	{

		//* Build corridors
		List<int> builderComplexities = new List<int>();

		bool done = false;

		WaitUntil waitUntil = new WaitUntil(() => done);

		for (int i = 0; i < finalGraph.Count; i++)
		{
			for (int j = 0; j < finalGraph[i].NEIGHBOURS.Count; j++)
			{
				AStarCorridorBuilder builder = new AStarCorridorBuilder();
				done = builder.BuildAStarCorridor(finalGraph[i].ROOM, finalGraph[i].NEIGHBOURS.ElementAt(j).ROOM,
					pathfindingStairCost, pathfindingRoomCost, pathfindingCorridorCost, pathfindingBaseCost, corridorTile, stairTile, grid, min, max);
				yield return waitUntil;
				grid = builder.GRID;
				builderComplexities.Add(builder.CORRIDORCOMPLEXITY);
				done = false;
			}
		}

		foreach (var value in grid.Values)
		{
			if (value.isCorridor)
			{
				Instantiate(corridorTile, value.position, Quaternion.identity);
			}
			else if (value.isStair)
			{
				Instantiate(stairTile, value.position, Quaternion.identity);
			}
		}

		foreach (Room room in createdRooms)
		{
			room.RoomCollider.enabled = false;
		}

		int greatestComplexity = 0;
		for (int i = 0; i < builderComplexities.Count - 1; i++)
		{
			if (Mathf.Max(builderComplexities[i], builderComplexities[i + 1]) > greatestComplexity)
				greatestComplexity = Mathf.Max(builderComplexities[i], builderComplexities[i + 1]);
		}

		LogCorridorComplexity(greatestComplexity);
		LogMeanCorridorComplexity(builderComplexities.ToArray());

		madeDungeon = true;
	}

	//* Using SetPropertyBlock for performance reasons, this method means i directly edit the properties of the actual material instance --
	//* I'm not creating a new material instance at runtime or editing the colour of the material asset itself
	void ChooseStartEndRooms()
	{
		int roomIndex = rand.Next(0, acceptedRooms.Count);
		startRoom = acceptedRooms[roomIndex];
		while (acceptedRooms[roomIndex] == startRoom)
		{
			roomIndex = rand.Next(0, acceptedRooms.Count);
		}
		endRoom = acceptedRooms[roomIndex];

		//* Check if start & end rooms should not be directly connected
		if (noCorridorBetweenStartEnd)
		{
			DTPoint3D start = finalGraph.Where(p => p.ROOM == startRoom).FirstOrDefault();
			DTPoint3D end = finalGraph.Where(p => p.ROOM == endRoom).FirstOrDefault();
			if (start.NEIGHBOURS.Contains(end))
			{
				start.RemoveEdge(end);
				start.RemoveNeighbour(end);
			}
			if (end.NEIGHBOURS.Contains(start))
			{
				end.RemoveEdge(start);
				end.RemoveNeighbour(start);
			}
		}

		if (visualizeSpecialRooms)
		{
			for (int i = 0; i < startRoom.ChildTiles.Length; i++)
			{
				MaterialPropertyBlock mpb = new MaterialPropertyBlock();
				mpb.SetColor("_BaseColor", startRoomVisualizationColor);
				startRoom.ChildRenderers[i].SetPropertyBlock(mpb);
			}

			for (int i = 0; i < endRoom.ChildTiles.Length; i++)
			{
				MaterialPropertyBlock mpb = new MaterialPropertyBlock();
				mpb.SetColor("_BaseColor", endRoomVisualizationColor);
				endRoom.ChildRenderers[i].SetPropertyBlock(mpb);
			}
		}
	}

	void LogTimeTest()
	{
		Debug.Log(Time.realtimeSinceStartupAsDouble + ": sekunder");
	}

	void LogSize()
	{
		Bounds sizeBounds = new Bounds(acceptedRooms[0].RoomObject.position, acceptedRooms[0].RoomCollider.size);

		for (int i = 1; i < acceptedRooms.Count; i++)
		{
			sizeBounds.Encapsulate(acceptedRooms[i].RoomObject.position + acceptedRooms[i].RoomCollider.size / 2);
			sizeBounds.Encapsulate(acceptedRooms[i].RoomObject.position - acceptedRooms[i].RoomCollider.size / 2);
		}

		Debug.Log(/*sizeBounds.size + " | " + */(sizeBounds.size.x * sizeBounds.size.y * sizeBounds.size.z) + "m³: Total rymdstorlek");
		//LogDensity(sizeBounds);
	}

	void LogDensity(Bounds sizeBounds)
	{
		Vector3 totalDensity = Vector3.zero;
		for (int i = 0; i < acceptedRooms.Count; i++)
		{
			totalDensity += acceptedRooms[i].RoomCollider.size;
		}
		float density = new Vector3(totalDensity.x / sizeBounds.size.x, totalDensity.y / sizeBounds.size.y, totalDensity.z / sizeBounds.size.z).magnitude;//totalDensity.magnitude / sizeBounds.size.magnitude;
		Debug.Log(density + ": Total densitet");
	}

	void LogTotalDensity(Dictionary<Vector3Int, AStarNode> grid)
	{
		float totalDensity = 0;
		float totalSize = 0;
		foreach (var value in grid.Values)
		{
			if (value.isRoom || value.isCorridor || value.isStair)
			{
				totalDensity++;
			}
			totalSize++;
		}
		float density = totalDensity / totalSize;
		Debug.Log(density + " kg/m³: Total densitet");
	}

	void LogPhysicalDistanceStartToEnd()
	{
		Debug.Log((endRoom.RoomObject.position - startRoom.RoomObject.position).magnitude + ": Fysisk distans från start till mål");
	}

	#region OldTests
	//! Not quite a depth search just yet
	//! THIS JUST IN, I'M A MORON! I NEED TO DO A SHORTEST PATH SEARCH NOT A DEPTH FIRST SEARCH, DIJKSTRA'S HERE I COME!!!
	//* Nevermind i'm not a moron it turns out, at least not entirely --
	//* i don't need a depth first search, i need to do a breadth first search since i'm only interested in the amount of rooms needed to be traversed --
	//* from start to end, which means i will be traversing an unweighted graph & that is not applicable to Dijkstra's algorithm
	//* First attempt inspired by youtube: https://www.youtube.com/watch?v=UvxV6y0k6Vk
	//* Second BFS attempt, inspired by geeksforgeeks: https://www.geeksforgeeks.org/breadth-first-search-or-bfs-for-a-graph/
	//* Third BFS attempt, inspired by geeksforgeeks: https://www.geeksforgeeks.org/number-shortest-paths-unweighted-directed-graph/
	//! Thought about the fact that the graph i'm traversing in both unweighted & directed
	//? Gonna try an own idea based on the previous attempts
	//* I'm onto something with this attempt, i could try a dual solution where i do a search from start to end & one from end to start & compare the shortest one --
	//* hopefully this'll increase the chance that they don't take the same route & that one of them find the actual shortest path...
	//* Dual solution seems promising, ran the test 4 times & it got the right result each time! Much better than failing after one or two tests
	void LogShortestTravelDistanceStartToEnd2()
	{
		List<DTPoint3D> rooms = new List<DTPoint3D>();
		List<int> jumps = new List<int>();
		List<DTPoint3D> previousJumpRooms = new List<DTPoint3D>();

		//* Initialize
		int startRoomIndex = 0;
		int endRoomIndex = 0;
		for (int i = 0; i < finalGraph.Count; i++)
		{
			if (finalGraph[i].ROOM == startRoom)
				startRoomIndex = i;
			else if (finalGraph[i].ROOM == endRoom)
				endRoomIndex = i;
			rooms.Add(finalGraph[i]);
			jumps.Add(-1);
			previousJumpRooms.Add(null);
		}

		int shortestFromStart = GetBFSFromStart(rooms, jumps, previousJumpRooms, startRoomIndex, endRoomIndex);
		int shortestFromEnd = GetBFSFromEnd(rooms, jumps, previousJumpRooms, endRoomIndex, startRoomIndex);

		int totalShortestJumps = shortestFromStart < shortestFromEnd ? shortestFromStart : shortestFromEnd;

		Debug.Log(totalShortestJumps + ": Kortaste vägen i antal rum från start till slut");
	}

	int GetBFSFromStart(List<DTPoint3D> rooms, List<int> jumps, List<DTPoint3D> previousJumpRooms, int startRoomIndex, int endRoomIndex)
	{
		//* Make starting node the shortest path node for start
		List<int> localJumps = new List<int>(jumps.Count);
		localJumps.AddRange(jumps);
		List<DTPoint3D> localRooms = new List<DTPoint3D>(rooms.Count);
		localRooms.AddRange(rooms);
		List<DTPoint3D> localPreviousRooms = new List<DTPoint3D>(previousJumpRooms.Count);
		localPreviousRooms.AddRange(previousJumpRooms);

		localJumps[startRoomIndex] = 0;
		localPreviousRooms[startRoomIndex] = localRooms[startRoomIndex];

		//* Make queue for BFS & add starting node
		Queue<DTPoint3D> points = new Queue<DTPoint3D>();
		points.Enqueue(localRooms[startRoomIndex]);

		//* Traverse graph with BFS!
		while (points.Count > 0)
		{
			DTPoint3D current = points.Dequeue();
			int currentIndex = 0;
			for (int i = 0; i < localRooms.Count; i++)
			{
				if (localRooms[i].INDEX == current.INDEX)
					currentIndex = i;
			}

			if (current.NEIGHBOURS.Count > 0)
			{
				//* Check direct neighbours
				for (int i = 0; i < current.NEIGHBOURS.Count; i++)
				{
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms.Count; j++)
					{
						if (localRooms[j].INDEX == current.NEIGHBOURS.ElementAt(i).INDEX)
							neighbourIndex = j;
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						points.Enqueue(localRooms[neighbourIndex]);
					}
				}

				//* Check any paths leading to current simultaneously
				for (int i = 0; i < localRooms.Count; i++)
				{
					int searchIndex = (currentIndex + i) % localRooms.Count;
					if (searchIndex == startRoomIndex) continue;
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms[searchIndex].NEIGHBOURS.Count; j++)
					{
						if (localRooms[searchIndex].NEIGHBOURS.ElementAt(j).INDEX == current.INDEX)
						{
							neighbourIndex = searchIndex;
						}
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}
			}
			else
			{
				//* Add points to queue which are yet to be visited

				//* Current has no neighbours, check any that connects to current
				for (int i = 0; i < localRooms.Count; i++)
				{
					int searchIndex = (currentIndex + i) % localRooms.Count;
					if (searchIndex == startRoomIndex) continue;
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms[searchIndex].NEIGHBOURS.Count; j++)
					{
						if (localRooms[searchIndex].NEIGHBOURS.ElementAt(j).INDEX == current.INDEX)
						{
							neighbourIndex = searchIndex;
						}
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}

				//* Check connections to/from any that still hasn't been searched
				for (int i = 0; i < localRooms.Count; i++)
				{
					if (i == startRoomIndex) continue;

					int neighbourIndex = -1;
					if (localJumps[i] == -1)
					{
						neighbourIndex = i;
					}

					if (neighbourIndex == -1) continue;

					for (int j = 0; j < localRooms[neighbourIndex].NEIGHBOURS.Count; j++)
					{
						for (int k = 0; k < localRooms.Count; k++)
						{
							if (localRooms[k].INDEX == localRooms[neighbourIndex].NEIGHBOURS.ElementAt(j).INDEX)
								currentIndex = k;
						}
					}

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}
			}
		}

		return localJumps[endRoomIndex];
	}

	int GetBFSFromEnd(List<DTPoint3D> rooms, List<int> jumps, List<DTPoint3D> previousJumpRooms, int startRoomIndex, int endRoomIndex)
	{
		//* Make starting node the shortest path node for start
		List<int> localJumps = new List<int>(jumps.Count);
		localJumps.AddRange(jumps);
		List<DTPoint3D> localRooms = new List<DTPoint3D>(rooms.Count);
		localRooms.AddRange(rooms);
		List<DTPoint3D> localPreviousRooms = new List<DTPoint3D>(previousJumpRooms.Count);
		localPreviousRooms.AddRange(previousJumpRooms);

		localJumps[startRoomIndex] = 0;
		localPreviousRooms[startRoomIndex] = localRooms[startRoomIndex];

		//* Make queue for BFS & add starting node
		Queue<DTPoint3D> points = new Queue<DTPoint3D>();
		points.Enqueue(localRooms[startRoomIndex]);

		//* Traverse graph with BFS!
		while (points.Count > 0)
		{
			DTPoint3D current = points.Dequeue();
			int currentIndex = 0;
			for (int i = 0; i < localRooms.Count; i++)
			{
				if (localRooms[i].INDEX == current.INDEX)
					currentIndex = i;
			}

			if (current.NEIGHBOURS.Count == 0)
			{
				//* Add points to queue which are yet to be visited

				//* Current has no neighbours, check any that connects to current
				for (int i = 0; i < localRooms.Count; i++)
				{
					int searchIndex = (currentIndex + i) % localRooms.Count;
					if (searchIndex == startRoomIndex) continue;
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms[searchIndex].NEIGHBOURS.Count; j++)
					{
						if (localRooms[searchIndex].NEIGHBOURS.ElementAt(j).INDEX == current.INDEX)
						{
							neighbourIndex = searchIndex;
						}
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}

				//* Check connections to/from any that still hasn't been searched
				for (int i = 0; i < localRooms.Count; i++)
				{
					if (i == startRoomIndex) continue;

					int neighbourIndex = -1;
					if (localJumps[i] == -1)
					{
						neighbourIndex = i;
					}

					if (neighbourIndex == -1) continue;

					for (int j = 0; j < localRooms[neighbourIndex].NEIGHBOURS.Count; j++)
					{
						for (int k = 0; k < localRooms.Count; k++)
						{
							if (localRooms[k].INDEX == localRooms[neighbourIndex].NEIGHBOURS.ElementAt(j).INDEX)
								currentIndex = k;
						}
					}

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}
			}
			else
			{
				//* Check direct neighbours
				for (int i = 0; i < current.NEIGHBOURS.Count; i++)
				{
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms.Count; j++)
					{
						if (localRooms[j].INDEX == current.NEIGHBOURS.ElementAt(i).INDEX)
							neighbourIndex = j;
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						points.Enqueue(localRooms[neighbourIndex]);
					}
				}

				//* Check any paths leading to current simultaneously
				for (int i = 0; i < localRooms.Count; i++)
				{
					int searchIndex = (currentIndex + i) % localRooms.Count;
					if (searchIndex == startRoomIndex) continue;
					int neighbourIndex = -1;
					for (int j = 0; j < localRooms[searchIndex].NEIGHBOURS.Count; j++)
					{
						if (localRooms[searchIndex].NEIGHBOURS.ElementAt(j).INDEX == current.INDEX)
						{
							neighbourIndex = searchIndex;
						}
					}

					if (neighbourIndex == -1) continue;

					//* Not updated/visited yet
					if (localJumps[neighbourIndex] == -1 || localJumps[neighbourIndex] > localJumps[currentIndex] + 1)
					{
						//* Update jumps to this current room as well as which room led to it
						localJumps[neighbourIndex] = localJumps[currentIndex] + 1;
						localPreviousRooms[neighbourIndex] = current;

						//* Add to queue, need to check this one as well
						points.Enqueue(localRooms[neighbourIndex]);
					}
				}
			}
		}

		return localJumps[endRoomIndex];
	}

	void LogCorridorAngleDelta(float angleDelta)
	{
		Debug.Log(angleDelta + "°: Högsta/brantaste korridorslutning");
	}

	void LogAverageCorridorAngle(float[] angles)
	{
		float averageAngle = 0.0f;
		for (int i = 0; i < angles.Length; i++)
		{
			averageAngle += angles[i];
		}
		averageAngle /= angles.Length;

		Debug.Log(averageAngle + "°: Medel korridorslutning");
	}

	void LogRoomHeightDifferenceBetweenNeighbours()
	{
		float highestDifference = 0, averageDifference = 0;
		HashSet<KeyValuePair<DTPoint3D, DTPoint3D>> checkedSets = new HashSet<KeyValuePair<DTPoint3D, DTPoint3D>>();
		for (int i = 0; i < finalGraph.Count; i++)
		{
			for (int j = 0; j < finalGraph[i].NEIGHBOURS.Count; j++)
			{
				if (finalGraph[i].INDEX == finalGraph[i].NEIGHBOURS.ElementAt(j).INDEX) continue;
				if (checkedSets.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[i], finalGraph[j])) ||
					checkedSets.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[j], finalGraph[i])))
					continue;
				checkedSets.Add(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[i], finalGraph[i].NEIGHBOURS.ElementAt(j)));
				float currentDifference = Mathf.Abs(finalGraph[i].ROOM.RoomObject.position.y - finalGraph[i].NEIGHBOURS.ElementAt(j).ROOM.RoomObject.position.y);
				if (currentDifference > highestDifference)
					highestDifference = currentDifference;
				averageDifference += currentDifference;
			}
		}
		averageDifference /= finalGraph.Count;

		Debug.Log(highestDifference + ": Störst höjdskillnad mellan grannar; " + averageDifference + ": Medelhöjdskillnad mellan grannar");
	}
	#endregion

	void LogCorridorComplexity(int complexity)
	{
		Debug.Log(complexity + ": Högsta korridorskomplexiteten");
	}

	void LogMeanCorridorComplexity(int[] complexities)
	{
		int averageComplexity = 0;

		for (int i = 0; i < complexities.Length; i++)
		{
			averageComplexity += complexities[i];
		}

		averageComplexity /= complexities.Length;

		Debug.Log(averageComplexity + ": Median korridorskomplexitet");
	}

	void LogRoomHeightDifference()
	{
		float highestDifference = 0, averageDifference = 0;
		HashSet<KeyValuePair<DTPoint3D, DTPoint3D>> checkedSets = new HashSet<KeyValuePair<DTPoint3D, DTPoint3D>>();
		for (int i = 0; i < finalGraph.Count; i++)
		{
			for (int j = 0; j < finalGraph.Count; j++)
			{
				if (finalGraph[i] == finalGraph[j]) continue;
				if (checkedSets.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[i], finalGraph[j])) ||
					checkedSets.Contains(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[j], finalGraph[i])))
					continue;
				checkedSets.Add(new KeyValuePair<DTPoint3D, DTPoint3D>(finalGraph[i], finalGraph[j]));
				float currentDifference = Math.Abs(finalGraph[i].ROOM.RoomObject.position.y - finalGraph[j].ROOM.RoomObject.position.y);
				if (currentDifference > highestDifference)
					highestDifference = currentDifference;
				averageDifference += currentDifference;
			}
		}
		averageDifference /= finalGraph.Count * finalGraph.Count;

		Debug.Log(highestDifference + ": Störst höjdskillnad; " + averageDifference + ": Medelhöjdskillnad");
	}

	void LogPercentOfCreatedRoomsAccepted()
	{
		int totalAccepted = acceptedRooms.Count + corridorRooms.Count;
		float usedRoomsPercentage = (float)((float)totalAccepted / (float)availableRoomsToSpawn);
		Debug.Log(usedRoomsPercentage + ": Procent av skapade rum använda för dungeon");
	}
}