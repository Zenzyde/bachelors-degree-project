using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// Del av examensarbete för kandidatexamen vid Högskolan i Skövde med inriktning dataspelsutveckling, år 2021. Skapat av Emil Birgersson
/// </summary>

namespace zenzyde
{
	public class DelaunayTriangulator3D
	{
		private DTPoint3D[] points;
		private DTTetrahedron root;
		private Vector3Int centerPoint;

		public DelaunayTriangulator3D(DTPoint3D[] points, Vector3Int centerPoint)
		{
			this.points = points;
			this.centerPoint = centerPoint;
		}

		//* Start with inserting one point, triangulate that and then insert another point
		public DTPoint3D[] Triangulate()
		{
			//* Create supertriangle/root
			root = GetSuperPoints();

			//* Store and iteratively insert points later
			Stack<DTPoint3D> pointsToInsert = new Stack<DTPoint3D>();
			for (int i = 0; i < points.Length; i++)
			{
				pointsToInsert.Push(points[i]);
			}

			while (pointsToInsert.Count > 0)
			{
				DTPoint3D currentPoint = pointsToInsert.Pop();
				DeleteEdges(currentPoint);
				CreateNewEdges(currentPoint);
			}

			RemoveOuterEdges();

			return points;
		}

		DTTetrahedron GetSuperPoints()
		{
			DTPoint3D[] outerPoints = new DTPoint3D[4];

			Vector3 point = centerPoint + Vector3.up * 50;
			while (Physics.CheckBox(point, Vector3.one * 35))
			{
				point += Vector3.up * 75;
			}
			outerPoints[0] = new DTPoint3D((int)point.x, (int)point.y, (int)point.z, -1, null);

			//Debug.DrawLine(centerPoint, outerPoints[0].POSITION, Color.red, Time.time + 10f);
			Vector3 forward;

			//* Could've use cosine & sine but oh well this works so whatevs...
			for (int i = 0; i < 3; i++)
			{
				Quaternion lookRotation = Quaternion.Euler(200, 120 * i, 0);
				forward = lookRotation * Vector3.one;

				point = centerPoint + forward * 50;

				while (Physics.CheckBox(point, Vector3.one * 35))
				{
					point += forward * 75;
				}
				outerPoints[1 + i] = new DTPoint3D((int)point.x, (int)point.y, (int)point.z, -2 - i, null);

				//Debug.DrawLine(centerPoint, outerPoints[1 + i].POSITION, Color.red, Time.time + 10f);
			}

			foreach (DTPoint3D firstpoint in outerPoints)
			{
				foreach (DTPoint3D otherPoint in outerPoints)
				{
					if (otherPoint.INDEX == firstpoint.INDEX) continue;
					firstpoint.AddNeighbour(otherPoint);
					firstpoint.AddEdge(otherPoint);
					otherPoint.AddNeighbour(firstpoint);
					otherPoint.AddEdge(firstpoint);
					//Debug.DrawLine(firstpoint.POSITION, otherPoint.POSITION, Color.green, Time.time + 10f);
				}
			}

			return new DTTetrahedron(outerPoints);
		}

		void CreateNewEdges(DTPoint3D point)
		{
			DTTetrahedron[] tetrahedrons = GetOverlappingTetrahedrons(point);

			foreach (DTTetrahedron tetrahedron in tetrahedrons)
			{
				foreach (DTPoint3D corner in tetrahedron.CORNERS)
				{
					point.AddNeighbour(corner);
					point.AddEdge(corner);
					corner.AddNeighbour(point);
					corner.AddEdge(point);
					//Debug.DrawLine(point.POSITION, corner.POSITION, Color.magenta, 10f);
				}
			}
		}

		void DeleteEdges(DTPoint3D point)
		{
			DTTetrahedron[] tetrahedrons = GetOverlappingTetrahedrons(point);

			foreach (DTTetrahedron tetrahedron in tetrahedrons)
			{
				foreach (DTPoint3D corner in tetrahedron.CORNERS)
				{
					corner.RemoveNeighbour(point);
					corner.RemoveEdge(point);
					point.RemoveNeighbour(corner);
					point.RemoveEdge(corner);
					//Debug.DrawLine(point.POSITION, corner.POSITION, Color.black, 10f);
				}
			}
		}

		void RemoveOuterEdges()
		{
			for (int j = root.CORNERS.Length - 1; j >= 0; j--)
			{
				DTPoint3D point = root.CORNERS[j];
				if (point.NEIGHBOURS.Count == 0) continue;
				for (int i = point.NEIGHBOURS.Count - 1; i >= 0; i--)
				{
					//! RemoveNeighbour-method seems weird, might be removing too many neighbours which fucks up the indexing
					//* Fixed by performing a dupe-check before adding neighbours
					DTPoint3D otherPoint = point.NEIGHBOURS.ElementAt(i);
					if (point.POSITION == otherPoint.POSITION && point.INDEX == otherPoint.INDEX) continue;
					point.RemoveNeighbour(otherPoint);
					point.RemoveEdge(otherPoint);
					otherPoint.RemoveNeighbour(point);
					otherPoint.RemoveEdge(point);
					//Debug.DrawLine(point.POSITION, otherPoint.POSITION, Color.black, 10f);
				}
			}

			// HashSet<DTPoint3D> visitedPoints = new HashSet<DTPoint3D>();
			// Queue<DTPoint3D> pointsToVisit = new Queue<DTPoint3D>();
			// pointsToVisit.Enqueue(root.CORNERS[0]);
			// visitedPoints.Add(root.CORNERS[0]);
			// while (pointsToVisit.Count > 0)
			// {
			// 	DTPoint3D currentPoint = pointsToVisit.Dequeue();
			// 	visitedPoints.Add(currentPoint);
			// 	foreach (DTPoint3D neighbour in currentPoint.NEIGHBOURS)
			// 	{
			// 		if (visitedPoints.Contains(neighbour)) continue;
			// 		pointsToVisit.Enqueue(neighbour);
			// 	}
			// }

			// for (int i = 0; i < visitedPoints.Count; i++)
			// {
			// 	DTPoint3D point = visitedPoints.ElementAt(i);
			// 	for (int j = 0; j < root.CORNERS.Length; j++)
			// 	{
			// 		DTPoint3D otherPoint = root.CORNERS[j];
			// 		if (point.POSITION == otherPoint.POSITION && point.INDEX == otherPoint.INDEX) continue;
			// 		point.RemoveNeighbour(otherPoint);
			// 		otherPoint.RemoveNeighbour(point);
			// 		Debug.DrawLine(point.POSITION, otherPoint.POSITION, Color.black, 100f);
			// 	}
			// }
		}

		DTTetrahedron[] GetOverlappingTetrahedrons(DTPoint3D point)
		{
			//* Create the sets of 4 points and verify if the point is within any of the sets
			Dictionary<int, DTTetrahedron> tetrahedrons = new Dictionary<int, DTTetrahedron>();

			Queue<DTPoint3D> pointsQueue = new Queue<DTPoint3D>();
			foreach (DTPoint3D rootPoint in root.CORNERS)
			{
				pointsQueue.Enqueue(rootPoint);
			}
			int currentSetSum = 0;
			while (pointsQueue.Count > 0)
			{
				DTPoint3D currentRoot = pointsQueue.Dequeue();
				int count = currentRoot.NEIGHBOURS.Count;
				for (int i = 0; i < count; i++)
				{
					DTPoint3D first = currentRoot.NEIGHBOURS.ElementAt(i % count);//[i % count];
					DTPoint3D second = currentRoot.NEIGHBOURS.ElementAt((i + 1) % count);//[(i + 1) % count];
					DTPoint3D third = currentRoot.NEIGHBOURS.ElementAt((i + 2) % count);//[(i + 2) % count];
					DTPoint3D fourth = currentRoot.NEIGHBOURS.ElementAt((i + 3) % count);//[(i + 3) % count];

					currentSetSum = first.INDEX + second.INDEX + third.INDEX + fourth.INDEX;
					if (!tetrahedrons.ContainsKey(currentSetSum))
					{
						tetrahedrons.Add(
							currentSetSum,
							new DTTetrahedron(
								new DTPoint3D[]
								{
									first,
									second,
									third,
									fourth
								}
							)
						);
						pointsQueue.Enqueue(first);
						pointsQueue.Enqueue(second);
						pointsQueue.Enqueue(third);
						pointsQueue.Enqueue(fourth);
					}
				}
			}

			if (tetrahedrons.Count == 0)
			{
				Debug.Log("Point set is empty");
				return null;
			}

			//* Look through the tetrahedrons and check if point is withing any of the sets
			List<int> indexesToRemove = new List<int>();
			foreach (KeyValuePair<int, DTTetrahedron> set in tetrahedrons)
			{
				tetrahedrons.TryGetValue(set.Key, out DTTetrahedron value);
				int index = CheckCircumSphere(value, point);
				if (index == -10)
				{
					indexesToRemove.Add(set.Key);
				}
			}
			for (int i = 0; i < indexesToRemove.Count; i++)
			{
				tetrahedrons.Remove(indexesToRemove[i]);
			}

			return tetrahedrons.Values.ToArray();
		}

		int CheckCircumSphere(DTTetrahedron points, DTPoint3D room)
		{
			Vector3Int center = new Vector3Int();
			for (int i = 0; i < points.CORNERS.Length; i++)
			{
				center += points.CORNERS[i].POSITION;
			}
			center /= points.CORNERS.Length;

			int radius = (int)(points.CORNERS[0].POSITION - center).magnitude;
			if (CheckInsideSphere(room.POSITION, center, radius))
				return room.INDEX;
			return -10;
		}

		bool CheckInsideSphere(Vector3Int position, Vector3Int center, int radius)
		{
			if (position.x <= center.x + radius && position.x >= center.x - radius &&
			position.y <= center.y + radius && position.y >= center.y - radius &&
			position.z <= center.z + radius && position.z >= center.z - radius)
				return true;
			return false;
		}
	}

	public class DTPoint3D
	{
		public Vector3Int POSITION { get { return position; } }
		private Vector3Int position = Vector3Int.zero;

		public HashSet<DTEdge3D> EDGES { get { return edges; } }
		private HashSet<DTEdge3D> edges = new HashSet<DTEdge3D>();

		public HashSet<DTPoint3D> NEIGHBOURS { get { return neighbours; } }
		private HashSet<DTPoint3D> neighbours = new HashSet<DTPoint3D>();

		public int INDEX { get { return index; } }
		private int index = -10;

		public Room ROOM { get { return room; } }
		private Room room;

		public DTPoint3D()
		{

		}

		public DTPoint3D(int X, int Y, int Z, int index, Room room)
		{
			position = new Vector3Int(X, Y, Z);
			this.index = index;
			this.room = room;
		}

		public DTPoint3D(Vector3Int pos, int index, Room room)
		{
			position = pos;
			this.index = index;
			this.room = room;
		}

		public void AddEdge(DTPoint3D neighbour)
		{
			edges.Add(
				new DTEdge3D(
					this, neighbour
				)
			);
		}

		public void RemoveEdge(Vector3Int start, Vector3Int end)
		{
			// for (int i = edge3Ds.Count - 1; i >= 0; i--)
			// {
			// 	if (edge3Ds[i].START.POSITION == start && edge3Ds[i].END.POSITION == end)
			// 		edge3Ds.RemoveAt(i);
			// }
			edges.RemoveWhere(x => x.START.position == start && x.END.POSITION == end);
		}

		public void RemoveEdge(DTPoint3D neighbour)
		{
			// for (int i = edge3Ds.Count - 1; i >= 0; i--)
			// {
			// 	if (edge3Ds[i].START.POSITION == edge.START.POSITION && edge3Ds[i].END.POSITION == edge.END.POSITION)
			// 		edge3Ds.RemoveAt(i);
			// }
			edges.RemoveWhere(x => x.END == neighbour);
		}

		public void AddNeighbour(DTPoint3D point)
		{
			// for (int i = 0; i < neighbours.Count; i++)
			// {
			// 	if (neighbours[i].POSITION == point.POSITION && neighbours[i].INDEX == point.INDEX) return;
			// }
			neighbours.Add(point);
		}

		public void RemoveNeighbour(DTPoint3D point)
		{
			// for (int i = neighbours.Count - 1; i >= 0; i--)
			// {
			// 	if (neighbours[i].POSITION == point.POSITION && neighbours[i].INDEX == point.INDEX)
			// 	{
			// 		neighbours.RemoveAt(i);
			// 	}
			// }
			neighbours.Remove(point);
		}
	}

	public class DTEdge3D
	{
		public DTPoint3D START { get { return start; } }
		public DTPoint3D END { get { return end; } }
		private DTPoint3D start = null, end = null;
		public Vector3 DIRECTION { get { return direction; } }
		private Vector3 direction = Vector3.zero;
		public int MAGNITUDE { get { return magnitude; } }
		private int magnitude = 0;

		public DTEdge3D()
		{

		}

		public DTEdge3D(DTPoint3D start, DTPoint3D end)
		{
			this.start = start;
			this.end = end;
			this.direction = ((Vector3)end.POSITION - (Vector3)start.POSITION).normalized;
			this.magnitude = (int)(end.POSITION - start.POSITION).magnitude;
		}
	}

	public class DTTetrahedron
	{
		public DTPoint3D[] CORNERS { get { return corners; } }
		private DTPoint3D[] corners;

		public Vector3Int CENTER { get { return center; } }
		private Vector3Int center;

		public DTTetrahedron(DTPoint3D[] corners)
		{
			this.corners = corners;
			center = new Vector3Int();
			for (int i = 0; i < this.corners.Length; i++)
			{
				center += this.corners[i].POSITION;
				for (int j = 0; j < this.corners.Length; j++)
				{
					if (i == j) continue;
					// DTEdge3D edge = new DTEdge3D(
					// 	this.corners[i],
					// 	this.corners[j]
					// );
					this.corners[i].AddEdge(this.corners[j]);
				}
			}
			center /= this.corners.Length;
		}
	}
}