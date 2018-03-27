using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BezierCurve))]
public class BezierCurveInspector : Editor {

	private BezierCurve Curve;
	private Transform HandleTransform;
	private Quaternion HandleRotation;

	void OnSceneGUI () {
		Curve = target as BezierCurve;

		// If we're allowing editor tools, disable drawing or everything gets messed up
		if (Curve.AllowEditorTools)	return;

		// Editor tools not allowed, force no tool
		Tools.current = Tool.None;

		HandleTransform = Curve.transform;
		HandleRotation = Tools.pivotRotation == PivotRotation.Local ? HandleTransform.rotation : Quaternion.identity;

		Vector3 mousePosition = Event.current.mousePosition;

		Vector3 LastEndPoint = Vector3.zero;

		// Iterate through each unique curve
		for (int i = 0; i < Curve.transform.childCount - 1; i++) {
			Vector3 pp0 = GetPointPos ((i * 3) + 0);
			Vector3 pp1 = GetPointPos ((i * 3) + 1);
			Vector3 pp2 = GetPointPos ((i * 3) + 2);
			Vector3 pp3 = GetPointPos ((i * 3) + 3);

			Vector3 p0 = Vector3.zero;
			Vector3 p1 = Vector3.zero;
			Vector3 p2 = Vector3.zero;
			Vector3 p3 = Vector3.zero;

			if (i > Curve.DisplayCenter - Curve.DisplayMargin && i < Curve.DisplayCenter + Curve.DisplayMargin || !Curve.IsPlayerPath) {
				p0 = ShowPoint ((i * 3) + 0, pp0);
				if (pp1 != Vector3.zero && pp2 != Vector3.zero) {
					p1 = ShowPoint ((i * 3) + 1, pp1);
					p2 = ShowPoint ((i * 3) + 2, pp2);
				}
				p3 = ShowPoint ((i * 3) + 3, pp3);

				if (pp1 != Vector3.zero && pp2 != Vector3.zero) {
					Handles.DrawBezier (p0, p3, p1, p2, (i % 3 == 0 ? Color.white : (i % 3 == 1 ? Color.red : Color.blue)), null, 2f);

					if (Curve.DrawLinkLines) {
						Handles.color = Color.green;

						Handles.DrawLine (p1, p0);
						Handles.DrawLine (p2, p3);
					}
				}
			} else if(Curve.OpacityOfHidden > 0f){
				p0 = DontShowPoint ((i * 3) + 0, pp0);
				if (pp1 != Vector3.zero && pp2 != Vector3.zero) {
					p1 = DontShowPoint ((i * 3) + 1, pp1);
					p2 = DontShowPoint ((i * 3) + 2, pp2);
				}
				p3 = DontShowPoint ((i * 3) + 3, pp3);

				if (pp1 != Vector3.zero && pp2 != Vector3.zero)
					Handles.DrawBezier (p0, p3, p1, p2, (i % 3 == 0 ? new Color (1f, 1f, 1f, Curve.OpacityOfHidden) : (i % 3 == 1 ? new Color (1f, 0f, 0f, Curve.OpacityOfHidden) : new Color (0f, 0f, 1f, Curve.OpacityOfHidden))), null, 2f);
			}

			LastEndPoint = pp3;
		}

		HandleUtility.Repaint ();
	}

	private Vector3 GetPointPos(int i){
		return HandleTransform.TransformPoint (Curve.GetPointPos (i));
	}

	private Vector3 DontShowPoint(int i, Vector3 Point){
		Handles.color = (i % 3 == 0 ? new Color(0f, 1f, 0f, Curve.OpacityOfHidden) : new Color(0.5f, 0.5f, 0.5f, Curve.OpacityOfHidden));
		Handles.SphereCap (1180, Point, HandleRotation, 1f);

		return Point;
	}

	private Vector3 ShowPoint (int i, Vector3 Point) {
		EditorGUI.BeginChangeCheck();

		Handles.color = (i % 3 == 0 ? Color.green : Color.gray);
		Handles.SphereCap (1180, Point, HandleRotation, 1f);
		
		//if(HandleUtility.DistanceToCircle(Point, 1f) <= 50f)
		//	Point = Handles.PositionHandle (Point, HandleRotation);//Handles.DoPositionHandle(Point, HandleRotation);
		Point = Handles.DoPositionHandle (Point, HandleRotation);


		if (EditorGUI.EndChangeCheck()) {
			Undo.RecordObject(Curve, "Move Point");
			EditorUtility.SetDirty(Curve);
			Curve.Points[i] = HandleTransform.InverseTransformPoint(Point);
		}

		return Point;
	}
}