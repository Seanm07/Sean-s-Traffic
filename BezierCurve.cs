using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;
#if UNITY_EDITOR
	using UnityEditor;
#endif

[ExecuteInEditMode]
public class BezierCurve : MonoBehaviour {

	//[HideInInspector]
	//public Vector3[] Points;
	//[FormerlySerializedAs("Points")]
	//public Vector3[] OldPoints;

	public List<Vector3> Points = new List<Vector3>();

	[Tooltip("To insert into the middle of the list create a new pos object then set ScriptDisabled, delete the last 3 list keys and add 3 list keys in the middle of the list")]
	public bool ScriptDisabled = false; // Whilst the script is disabled we can manually mess around with the array values

	#if UNITY_EDITOR
		public bool AllowEditorTools = false;
		private bool PrevAllowEditTools = false;
	#endif

	public bool IsPlayerPath = false;

	[Header("Editor display settings")]
	public int DisplayMargin = 5;
	public int DisplayCenter = 5;
	public bool DrawLinkLines = false;
	public float OpacityOfHidden = 0.25f;

	public Vector3 GetPointPos (int i)
	{
		#if UNITY_EDITOR
			if(i >= Points.Count) {
				ReCalculate(false);
			}
		#endif

		return Points [i];
	}

	// If both beziers from the previous point are at Vector3.zero then it's a teleport point
	// Before you change this and break everything again, note that it's supossed to take you to the previous wave rather than the wanted wave, we then just force the player to the end of it (which is the correct position)
	public bool IsTeleportPoint(int StartingPoint = 0){
		StartingPoint *= 3;

		if (StartingPoint >= 3) {
			//Debug.Log ("btw point A (" + (StartingPoint + 1) + ": " + Points [StartingPoint + 1] + " and B(" + (StartingPoint + 2) + ": " + Points [StartingPoint + 2]);

			return (Points [StartingPoint + 1] == Vector3.zero && Points [StartingPoint + 2] == Vector3.zero);
		} else {
			// This can't be a teleport point if there were no previous points
			return false;
		}
	}

	public Vector3 GetPoint (float t, int StartingPoint = 0, bool InWorldSpace = false) {
		StartingPoint *= 3;

		if (StartingPoint + 3 > Points.Count - 1) {
			Debug.LogError ("Invalid wanted point, there isn't enough points available!");
			return Vector3.zero;
		} else {
			return Bezier.GetPoint (Points [StartingPoint], Points [StartingPoint + 1], Points [StartingPoint + 2], Points [StartingPoint + 3], t) + (InWorldSpace ? transform.position : Vector3.zero);
		}
	}

	public float GetLength (int StartingPoint = 0, int Accuracy = 10) {
		StartingPoint *= 3;

		if (StartingPoint + 3 > Points.Count - 1) {
			Debug.LogError ("Invalid wanted point, there isn't enough points available!");
			return 0f;
		} else {
			return Bezier.GetLength (Points [StartingPoint], Points [StartingPoint + 1], Points [StartingPoint + 2], Points [StartingPoint + 3], Accuracy);
		}
	}
	
	public Vector3 GetVelocity (float t, int StartingPoint = 0) {
		StartingPoint *= 3;

		if (StartingPoint + 3 > Points.Count - 1) {
			Debug.LogError ("Invalid wanted point, there isn't enough points available!");
			return Vector3.zero;
		} else {
			return Bezier.GetFirstDerivative (Points [StartingPoint], Points [StartingPoint + 1], Points [StartingPoint + 2], Points [StartingPoint + 3], t);
		}
	}

	public Quaternion GetDirection (float t, int StartingPoint = 0) {
		Vector3 Velocity = GetVelocity (t, StartingPoint).normalized;
		Quaternion ReturnValue = Quaternion.identity;

		if (Velocity != Vector3.zero)
			ReturnValue = Quaternion.LookRotation (Velocity);

		return ReturnValue;
	}

	#if UNITY_EDITOR
		[ContextMenu("Recalculate")]
		public void ReCalculate()
		{
			ReCalculate (false);
		}

		public void ReCalculate (bool ResetAllPoints) {
			if (ScriptDisabled) return;

			int TotalPaths = transform.childCount;
			int FinalTotalPoints = (TotalPaths - 1) * 3 + 1;

			IsPlayerPath = IsPlayerPath;

			List<Vector3> FinalPoints = new List<Vector3> ();
			//Vector3[] FinalPoints = new Vector3[FinalTotalPoints];

			for (int i = 0; i < FinalTotalPoints; i++)
				FinalPoints.Add (Vector3.zero);

			/*// 0 FROM_OBJ
			// 1 CURVE
			// 2 CURVE
			// 3 TO_OBJ + FROM_OBJ
			// 4 CURVE
			// 5 CURVE
			// 6 TO_OBJ + FROM_OBJ
			// 7 CURVE
			// 8 CURVE
			// 9 TO_OBJ + FROM_OBJ
			// 10 CURVE
			// 11 CURVE
			// 12 TO_OBJ

			// Objects: 5
			// curves: 8
			// total: 13

			// (13 / 3) + (2 / 3) = get total objects
			// (2 / 3) is for the extra last TO_OBJ

			// ((5 - 1) * 3) + 1
			// (5 - 1) to ignore the last T_OBJ
			// + 1 is because counting starts at 1 not 0

			// 5 - (((5 - 1) * 3) + 1) = total curves*/

			// Iterate through all paths
			for (int i = 0; i < TotalPaths-1; i++) {
				Transform FromObj = transform.GetChild (i + 0);
				Transform ToObj = transform.GetChild (i + 1);

				FinalPoints [(i * 3) + 0] = !ResetAllPoints && Points.Count > (i * 3) + 0 ? Points [(i * 3) + 0] : FromObj.position;
				FinalPoints [(i * 3) + 1] = !ResetAllPoints && Points.Count > (i * 3) + 1 ? Points [(i * 3) + 1] : ((ToObj.position - FromObj.position) * 0.333f) + FromObj.position;
				FinalPoints [(i * 3) + 2] = !ResetAllPoints && Points.Count > (i * 3) + 2 ? Points [(i * 3) + 2] : ((ToObj.position - FromObj.position) * 0.666f) + FromObj.position;
			}

			

			// Add the final ToObj (Outside the loop because that just added the from & to as a single obj at the start of each iteration)
			Transform FinalToObj = transform.GetChild (TotalPaths - 1);

			FinalPoints [FinalTotalPoints - 1] = FinalToObj.position;

			Points = FinalPoints;

			Debug.Log ("Points recalculated! (" + gameObject.name + ")");
		}

		// Pushing to the start of the array is complicated because I didn't use lists.. so this is a hacky quick manual workaround
		//[ContextMenu("Push Array Down")]
		//public void PushArrayDown()
		//{
		//Points
		//}

		[ExecuteInEditMode]
		public void Update()
		{
			if (Application.isPlaying) return;
			if (ScriptDisabled) return;

			if (Points.Count != 4 && !IsPlayerPath) ReCalculate (false);

			if (AllowEditorTools != PrevAllowEditTools)
				OnEditorToolsChangeState (AllowEditorTools);
			
			PrevAllowEditTools = AllowEditorTools;

			if (!AllowEditorTools) {
				int TotalPaths = transform.childCount;
				int FinalTotalPoints = (TotalPaths - 1) * 3 + 1;

				// Iterate through all paths
				for (int i = 0; i < TotalPaths-1; i++) {
					Transform FromObj = transform.GetChild (i + 0);
					Transform ToObj = transform.GetChild (i + 1);

					FromObj.position = Points [(i * 3) + 0];
					ToObj.position = Points [(i * 3) + 3];

					FromObj.rotation = GetDirection (0f);
					ToObj.rotation = GetDirection (1f);
				}
			} else {
				if (Tools.current == Tool.Rotate || Tools.current == Tool.Scale) {
					Tools.current = Tool.None;

					((SceneView)SceneView.sceneViews [0] as SceneView).ShowNotification (new GUIContent ("Rotation and scale should be applied to children not the parent!"));
				}

				transform.rotation = Quaternion.identity;
			}
		}

		public void OnEditorToolsChangeState(bool IsEditorToolsEnabled)
		{
			if (ScriptDisabled) return;

			if (!IsEditorToolsEnabled) {
				int TotalPaths = transform.childCount;
				int FinalTotalPoints = (TotalPaths - 1) * 3 + 1;

				// Iterate through all paths
				for (int i = 0; i < TotalPaths-1; i++) {
					Transform FromObj = transform.GetChild (i + 0);
					Transform ToObj = transform.GetChild (i + 1);

					// Sync the transform back to 0,0,0 and update the Points instead
					if (transform.position != Vector3.zero) {
						for (int p = 0; i < Points.Count; p++)
							Points [p] += transform.position;

						transform.position = Vector3.zero;

						FromObj.position = Points [(i * 3) + 0];
						ToObj.position = Points [(i * 3) + 3];
					}

					Points [(i * 3) + 0] = FromObj.position;
					Points [(i * 3) + 3] = ToObj.position;
				}
			}
		}
	#endif
}