using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(LaneBezierHandler))]
//[CanEditMultipleObjects]
public class EditorLaneInspector : Editor {

	void OnEnable()
	{
		
	}

	// This function is called for all selected targets so targets is not used (and cannot be used) in this function
	void OnSceneGUI()
	{
		LaneBezierHandler Curve = target as LaneBezierHandler;

		if (!Curve.IsStartPoint || Curve.EndPoint == null) {
			Tools.current = Tool.Move;
		} else {
			DrawHandles (Curve);
		}
	}

	void DrawHandles(LaneBezierHandler Curve)
	{
		Tools.current = Tool.None;

		Transform HandleTransform = Curve.transform;
		Quaternion HandleRotation = Tools.pivotRotation == PivotRotation.Local ? HandleTransform.rotation : Quaternion.identity;

		if (Curve.BezierPoints.Length >= 2) {
			Handles.DrawBezier (Curve.GetPoint (0), Curve.GetPoint (3), Curve.GetPoint (1), Curve.GetPoint (2), Color.red, null, 2f);

			ShowPoint (0, Curve, HandleRotation);
			ShowPoint (1, Curve, HandleRotation);
			ShowPoint (2, Curve, HandleRotation);
			ShowPoint (3, Curve, HandleRotation);
		} else if (Curve.BezierPoints.Length >= 1) {
			Handles.DrawBezier (Curve.GetPoint (0), Curve.GetPoint (3), Curve.GetPoint (1), Curve.GetPoint (1), Color.red, null, 2f);

			ShowPoint (0, Curve, HandleRotation);
			ShowPoint (1, Curve, HandleRotation);
			ShowPoint (3, Curve, HandleRotation);
		} else {
			Handles.color = Color.red;
			Handles.DrawLine (Curve.GetPoint (0), Curve.GetPoint (3));

			ShowPoint (0, Curve, HandleRotation);
			ShowPoint (3, Curve, HandleRotation);
		}

		HandleUtility.Repaint ();
	}

	private void ShowPoint(int PointID, LaneBezierHandler Curve, Quaternion HandleRotation)
	{
		Vector3 Point = Curve.GetPoint (PointID);

		EditorGUI.BeginChangeCheck();
		Handles.color = Color.green;
		Handles.SphereCap (1180, Point, HandleRotation, 1f);

		Point = Handles.DoPositionHandle (Point, HandleRotation);

		if (EditorGUI.EndChangeCheck()) {
			Undo.RegisterCompleteObjectUndo(Curve.transform, "Move Point");
			Curve.SetPoint(PointID, Point);
			EditorUtility.SetDirty(Curve);
		}
	}

	public override void OnInspectorGUI()
	{
		EditorGUI.BeginChangeCheck();

		foreach (Object CurTarget in targets) {
			LaneBezierHandler Curve = CurTarget as LaneBezierHandler;
			SerializedObject Target = new SerializedObject (CurTarget);

			SerializedProperty BezierPoints = Target.FindProperty ("BezierPoints");

			Target.Update ();

			GUIStyle LaneNameStyle = new GUIStyle (EditorStyles.whiteBoldLabel);
			LaneNameStyle.normal.textColor = Color.yellow;

			EditorGUILayout.LabelField ("Lane name:", Curve.name.Replace("Start ", "").Replace("End ", ""), LaneNameStyle);

			if (Curve.IsStartPoint) {
				EditorGUILayout.LabelField ("Connected to:", (Curve.EndPoint != null ? Curve.EndPoint.name : "NOT CONNECTED"));

				Curve.IsIntersection = EditorGUILayout.Toggle ("Is this an intersection?", Curve.IsIntersection);

				if (Curve.IsIntersection) {
					Curve.WaitForClearIntersection = EditorGUILayout.Toggle ("Wait for intersection to clear?", Curve.WaitForClearIntersection);
					Curve.DontWaitForThisLane = EditorGUILayout.Toggle ("Don't wait for this lane", Curve.DontWaitForThisLane);
				}

				if (BezierPoints.arraySize > 0) {
					for (int i = 0; i < BezierPoints.arraySize; i++) {
						GUILayout.BeginHorizontal ();
						EditorGUILayout.ObjectField (BezierPoints.GetArrayElementAtIndex(i));

						if (GUILayout.Button ("-")) {
							Curve.RemoveBezierPoint (i);
						}
						GUILayout.EndHorizontal ();
					}
				}

				if (BezierPoints.arraySize < 2) {
					if (GUILayout.Button ("+ Bezier Point")) {
						Curve.AddBezierPoint ();
					}
				}

				if (GUILayout.Button ("Snap all points to road surface")) {
					Curve.SnapPointsToRoadSurface ();
				}

				EditorGUILayout.Separator ();

				if (GUILayout.Button ("Create new lane using lane end as start")) {
					Curve.CreateNewLaneFromEndPos ();
				}

				if (GUILayout.Button ("Create new lane using lane start as start")) {
					Curve.CreateNewLaneFromStartPos ();
				}

				if (GUILayout.Button ("Swap start and end positions")) {
					Curve.SwapStartAndEndPositions ();
				}
			} else {
				EditorGUILayout.LabelField ("Select the lane start point for additional options!");
			}

			EditorGUILayout.Separator ();

			Target.ApplyModifiedProperties ();
		}

		if (EditorGUI.EndChangeCheck ()) {
			// This allows us to save the scene (without it when we save the scene Unity does nothing as it thinks it's already up to date..)
			UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty ();
		}
	}
}
