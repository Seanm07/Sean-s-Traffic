using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TrafficLaneManager))]
public class TrafficLaneManagerInspector : Editor {

	public override void OnInspectorGUI()
	{
		EditorGUILayout.HelpBox("Large scale mobile friendly GTA style traffic system\nCreated by Sean McManus of i6 Media (Version 4.4)\nHover over properties below for more information!", MessageType.Info);

		// Hidden in the context menu because I know devs will press this, get confused then ask me for help
		//if(GUILayout.Button("Ungroup All Lanes of Specific Map"))
		//	(serializedObject.targetObject as TrafficLaneManager).UngroupAllLanes();

		if(GUILayout.Button("Make All Lane Start Z Forward Towards Lane Ends"))
			(serializedObject.targetObject as TrafficLaneManager).StartFaceEnds();

		if(GUILayout.Button("Rebuild Road Data"))
			(serializedObject.targetObject as TrafficLaneManager).AutoFindLanes();

		GUILayout.Space(10f);

		if(GUILayout.Button("Auto Populate Sounds & Particles"))
			(serializedObject.targetObject as TrafficLaneManager).AutoPopulateSoundsParticles();

		if(GUILayout.Button("Auto Populate Car Templates"))
			(serializedObject.targetObject as TrafficLaneManager).AutoPopulateCarTemplates();

		if(GUILayout.Button("Auto Create Layers & Set Masks"))
			(serializedObject.targetObject as TrafficLaneManager).AutoCreateLayers();

		DrawDefaultInspector();

		// Using this stopped me from being able to change inspector values for some reason
		//DrawPropertiesExcluding(serializedObject, new string[1]{"m_Script"});
	}

}
