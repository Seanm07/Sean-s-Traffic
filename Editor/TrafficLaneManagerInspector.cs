using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TrafficLaneManager))]
public class TrafficLaneManagerInspector : Editor {

	public override void OnInspectorGUI()
	{
		EditorGUILayout.HelpBox("Large scale mobile friendly GTA style traffic system\nCreated by Sean McManus of i6 Media (Version 3.1)", MessageType.Info);

		//DrawDefaultInspector();

		DrawPropertiesExcluding(serializedObject, new string[1]{"m_Script"});
	}

}
