using UnityEngine;

public static class Bezier {

	public static Vector3 GetPoint (Vector3 p0, Vector3 p1, Vector3 p2, float t) {
		t = Mathf.Clamp01(t);
		float oneMinusT = 1f - t;
		return
			oneMinusT * oneMinusT * p0 +
			2f * oneMinusT * t * p1 +
			t * t * p2;
	}

	public static Vector3 GetFirstDerivative (Vector3 p0, Vector3 p1, Vector3 p2, float t) {
		return
			2f * (1f - t) * (p1 - p0) +
			2f * t * (p2 - p1);
	}

	public static Vector3 GetPoint (Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
		t = Mathf.Clamp01(t);
		float OneMinusT = 1f - t;
		return
			OneMinusT * OneMinusT * OneMinusT * p0 +
			3f * OneMinusT * OneMinusT * t * p1 +
			3f * OneMinusT * t * t * p2 +
			t * t * t * p3;
	}

	public static Vector3 GetFirstDerivative (Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
		t = Mathf.Clamp01(t);
		float oneMinusT = 1f - t;
		return
			3f * oneMinusT * oneMinusT * (p1 - p0) +
			6f * oneMinusT * t * (p2 - p1) +
			3f * t * t * (p3 - p2);
	}

	public static float GetLength(Vector3 p0, Vector3 p1, Vector3 p2, int Accuracy = 10)
	{
		if (Accuracy <= 1) {
			Debug.LogError ("GetLength accuracy must be higher than 1!");
			return 0f;
		}

		Vector3[] BezierPoints = new Vector3[Accuracy];
		float TotalDistance = 0f;

		for (int i = 0; i < Accuracy; i++) {
			BezierPoints [i] = GetPoint (p0, p1, p2, (float)(i) / (float)(Accuracy));

			if (i > 0) TotalDistance += Vector3.Distance (BezierPoints [i - 1], BezierPoints [i]);
		}

		return TotalDistance;
	}

	// Cut the bezier into <Accuracy> parts then add the distance between all parts
	public static float GetLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int Accuracy = 10)
	{
		if (Accuracy <= 1) {
			Debug.LogError ("GetLength accuracy must be higher than 1!");
			return 0f;
		}

		Vector3[] BezierPoints = new Vector3[Accuracy];
		float TotalDistance = 0f;

		for (int i = 0; i < Accuracy; i++) {
			BezierPoints[i] = GetPoint (p0, p1, p2, p3, (float)(i) / (float)(Accuracy));

			if (i > 0) TotalDistance += Vector3.Distance (BezierPoints [i - 1], BezierPoints [i]);
		}

		return TotalDistance;
	}
}