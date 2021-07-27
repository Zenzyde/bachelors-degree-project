using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Del av examensarbete för kandidatexamen vid Högskolan i Skövde med inriktning dataspelsutveckling, år 2021. Skapat av Emil Birgersson
/// </summary>

public class AStarCorridorBuilder
{
	public Dictionary<Vector3Int, AStarNode> GRID { get { return grid; } }
	public int CORRIDORCOMPLEXITY { get { return corridorComplexity; } }

	private Vector3Int[] offsets;

	private int stairCost, roomCost, corridorCost, baseCost;
	private int corridorComplexity = 0;

	private Vector3Int min, max;

	private Transform hallway, stair;

	private Dictionary<Vector3Int, AStarNode> grid;
	private List<AStarNode> openSet = new List<AStarNode>();
	private HashSet<AStarNode> closedSet = new HashSet<AStarNode>();

	public bool BuildAStarCorridor(Room startRoom, Room endRoom, int stairCost, int roomCost, int corridorCost, int baseCost,
		Transform hallway, Transform stair, Dictionary<Vector3Int, AStarNode> grid, Vector3Int min, Vector3Int max)
	{
		this.stairCost = stairCost;
		this.roomCost = roomCost;
		this.corridorCost = corridorCost;
		this.baseCost = baseCost;
		this.hallway = hallway;
		this.stair = stair;
		this.grid = grid;
		this.min = min;
		this.max = max;
		offsets = new Vector3Int[]{
			new Vector3Int(-1, 0, 0),
			new Vector3Int(1, 0, 0),

			new Vector3Int(0, 0, -1),
			new Vector3Int(0, 0, 1),

			new Vector3Int(-3, 1, 0),
			new Vector3Int(-3, -1, 0),
			new Vector3Int(3, 1, 0),
			new Vector3Int(3, -1, 0),

			new Vector3Int(0, 1, -3),
			new Vector3Int(0, -1, -3),
			new Vector3Int(0, 1, 3),
			new Vector3Int(0, -1, -3)
		};

		grid.TryGetValue(Vector3Int.RoundToInt(startRoom.RoomObject.position), out AStarNode startNode);
		AStarNode start = startNode;
		grid.TryGetValue(Vector3Int.RoundToInt(endRoom.RoomObject.position), out AStarNode endNode);
		AStarNode end = endNode;
		List<AStarNode> path = FindPath(start, end);
		if (path != null || path.Count > 0)
		{
			BuildPath(path);
		}
		return true;
	}

	List<AStarNode> FindPath(AStarNode start, AStarNode goal)
	{
		openSet.Add(start);

		while (openSet.Count > 0)
		{
			AStarNode current = openSet[0];
			for (int i = 0; i < openSet.Count; i++)
			{
				if (openSet[i].totalDistFCost < current.totalDistFCost ||
					openSet[i].totalDistFCost == current.totalDistFCost && openSet[i].distFromGoalHCost < current.distFromGoalHCost)
				{
					current = openSet[i];
				}
			}

			openSet.Remove(current);
			closedSet.Add(current);

			if (current.position == goal.position)
			{
				return ReconstructPath(start, current);
			}

			foreach (Vector3Int offset in offsets)
			{
				//* Check if position is inside bounds, continue if not
				if (!InsideBoundary(current.position + offset))
				{
					continue;
				}

				if (grid.TryGetValue(current.position + offset, out AStarNode neighbour))
				{
					if (closedSet.Contains(neighbour) || current.previousSet.Contains(neighbour.position))
					{
						continue;
					}

					PathTraverseCost traverseCost = GetHeuristic(current, neighbour, goal);

					if (!traverseCost.isTraversable)
					{
						continue;
					}

					if (traverseCost.isStairs) //* If pathCost is stairs
					{
						int xDir = Mathf.Clamp(offset.x, -1, 1);
						int zDir = Mathf.Clamp(offset.z, -1, 1);

						Vector3Int verticalOffset = new Vector3Int(0, offset.y, 0);
						Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

						if (current.previousSet.Contains(current.position + horizontalOffset) ||
							current.previousSet.Contains(current.position + horizontalOffset * 2) ||
							current.previousSet.Contains(current.position + verticalOffset + horizontalOffset) ||
							current.previousSet.Contains(current.position + verticalOffset + horizontalOffset * 2))
						{
							continue;
						}
					}

					int newCost = (int)(current.distFromStartGCost + traverseCost.cost);
					if (newCost < neighbour.distFromStartGCost || !openSet.Contains(neighbour))
					{
						neighbour.distFromStartGCost = newCost;
						neighbour.distFromGoalHCost = (int)traverseCost.cost;
						neighbour.previous = current;

						openSet.Add(neighbour);

						neighbour.previousSet.Clear();
						neighbour.previousSet.UnionWith(current.previousSet);
						neighbour.previousSet.Add(current.position);

						if (traverseCost.isStairs) //* If pathCost is stairs
						{
							int xDir = Mathf.Clamp(offset.x, -1, 1);
							int zDir = Mathf.Clamp(offset.z, -1, 1);

							Vector3Int verticalOffset = new Vector3Int(0, offset.y, 0);
							Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

							neighbour.previousSet.Add(current.position + horizontalOffset);
							neighbour.previousSet.Add(current.position + horizontalOffset * 2);
							neighbour.previousSet.Add(current.position + verticalOffset + horizontalOffset);
							neighbour.previousSet.Add(current.position + verticalOffset + horizontalOffset * 2);
						}
					}
				}
			}
		}
		return null;
	}

	List<AStarNode> ReconstructPath(AStarNode start, AStarNode current)
	{
		List<AStarNode> path = new List<AStarNode>();
		AStarNode node = current;
		while (node != start)
		{
			path.Add(node);
			node = node.previous;
		}
		path.Reverse();
		return path;
	}

	struct PathTraverseCost
	{
		public PathTraverseCost(bool traversable, bool stairs, float cost)
		{
			isTraversable = traversable;
			isStairs = stairs;
			this.cost = cost;
		}

		public bool isTraversable, isStairs;
		public float cost;
	}

	PathTraverseCost GetHeuristic(AStarNode current, AStarNode neighbour, AStarNode goal)
	{
		Vector3Int delta = neighbour.position - current.position;

		float cost = 0;
		bool isTraversable = false;
		bool isStairs = false;

		//* Flat hallway
		if (delta.y == 0)
		{
			cost = Vector3Int.Distance(neighbour.position, goal.position); //* Heuristic

			bool stair = neighbour.isStair;

			bool room = neighbour.isRoom;

			bool corridor = neighbour.isCorridor;

			if (stair)
			{
				cost += stairCost;
				return new PathTraverseCost(isTraversable, isStairs, cost);
			}
			else if (room)
			{
				cost += roomCost;
			}
			else if (corridor)
			{
				cost += corridorCost;
			}
			else
			{
				cost += corridorCost;
			}

			isTraversable = true;
		}
		//* Staircase
		else
		{
			bool stair = !current.isCorridor && !current.isEmpty && !current.isRoom || !neighbour.isCorridor && !neighbour.isEmpty && !neighbour.isRoom;
			if (stair)
			{
				cost += stairCost;
				return new PathTraverseCost(isTraversable, isStairs, cost);
			}

			cost = baseCost + Vector3Int.Distance(neighbour.position, goal.position); //* Base cost + heuristic

			int xDir = Mathf.Clamp(delta.x, -1, 1);
			int zDir = Mathf.Clamp(delta.z, -1, 1);

			Vector3Int verticalOffset = new Vector3Int(0, delta.y, 0);
			Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

			//* Check if position isn't in bounds & return current cost if it isn't
			if (!InsideBoundary(current.position + verticalOffset) ||
				!InsideBoundary(current.position + horizontalOffset) ||
				!InsideBoundary(current.position + verticalOffset + horizontalOffset))
			{
				return new PathTraverseCost(isTraversable, isStairs, cost);
			}

			grid.TryGetValue(current.position + horizontalOffset, out AStarNode one);
			grid.TryGetValue(current.position + horizontalOffset * 2, out AStarNode two);
			grid.TryGetValue(current.position + verticalOffset + horizontalOffset, out AStarNode three);
			grid.TryGetValue(current.position + verticalOffset + horizontalOffset * 2, out AStarNode four);
			if (!one.isEmpty || !two.isEmpty || !three.isEmpty || !four.isEmpty)
			{
				return new PathTraverseCost(isTraversable, isStairs, cost);
			}

			isTraversable = true;
			isStairs = true;
		}

		return new PathTraverseCost(isTraversable, isStairs, cost);
	}

	void BuildPath(List<AStarNode> path)
	{
		if (path == null)
		{
			Debug.LogWarning("Path is null!");
			return;
		}

		Vector3 previousDirection = Vector3.zero;

		for (int i = 0; i < path.Count; i++)
		{
			AStarNode current = path[i];
			if (current.isEmpty)
			{
				//* Place hallway
				current.isEmpty = false;
				current.isCorridor = true;
				//GameObject.Instantiate(hallway, current.position, Quaternion.identity);
			}

			if (i > 0)
			{
				AStarNode previous = path[i - 1];
				Vector3Int delta = current.position - previous.position;
				if (delta.y != 0)
				{
					int xDir = Mathf.Clamp(delta.x, -1, 1);
					int zDir = Mathf.Clamp(delta.z, -1, 1);
					Vector3Int verticalOffset = new Vector3Int(0, delta.y, 0);
					Vector3Int horizontalOffset = new Vector3Int(xDir, 0, zDir);

					grid.TryGetValue(previous.position + horizontalOffset, out AStarNode one);
					grid.TryGetValue(previous.position + horizontalOffset * 2, out AStarNode two);
					grid.TryGetValue(previous.position + verticalOffset + horizontalOffset, out AStarNode three);
					grid.TryGetValue(previous.position + verticalOffset + horizontalOffset * 2, out AStarNode four);
					one.isStair = true;
					two.isStair = true;
					three.isStair = true;
					four.isStair = true;

					//* Place stairs
					//GameObject.Instantiate(stair, (previous.position + horizontalOffset), Quaternion.identity);
					//GameObject.Instantiate(stair, (previous.position + horizontalOffset * 2), Quaternion.identity);
					//GameObject.Instantiate(stair, (previous.position + verticalOffset + horizontalOffset), Quaternion.identity);
					//GameObject.Instantiate(stair, (previous.position + verticalOffset + horizontalOffset * 2), Quaternion.identity);
				}

				if (previousDirection == Vector3.zero)
				{
					previousDirection = ((Vector3)current.position - (Vector3)previous.position).normalized;
				}
				else
				{
					Vector3 currentDirection = ((Vector3)current.position - (Vector3)previous.position).normalized;
					if (currentDirection != previousDirection)
					{
						corridorComplexity++;
					}
				}
			}
		}

		foreach (var pair in grid.Values)
		{
			pair.previous = null;
			pair.previousSet.Clear();
			pair.cost = 0;
			pair.distFromGoalHCost = 0;
			pair.distFromStartGCost = 0;
		}
	}

	bool InsideBoundary(Vector3Int position)
	{
		return position.x < max.x && position.x > min.x &&
			position.y < max.y && position.y > min.y &&
			position.z < max.z && position.z > min.z;
	}
}

public class AStarNode
{
	public AStarNode previous;
	public Vector3Int position;
	public float cost;
	public int distFromStartGCost, distFromGoalHCost;
	public HashSet<Vector3Int> previousSet = new HashSet<Vector3Int>();
	public bool isStair, isRoom, isCorridor, isEmpty, isTraversable;

	public int totalDistFCost { get { return distFromStartGCost + distFromGoalHCost; } }

	public AStarNode(Vector3Int position)
	{
		this.position = position;
	}
}
