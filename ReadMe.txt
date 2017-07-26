Version 2 - 26th July 2017

- Simple scene setup using existing traffic layouts:
- Drag "Traffic Manager" into your scene from the Prefabs folder
- Drag "Hit Source" into your scene
- Select the "Traffic Manager" in your scene 
- Tick "Cars Stop When Hit" if you want cars to breakdown when the player hits them, otherwise nothing will happen when they're hit (suggested enabled for car games and disabled for bike games unless you're planning to write some manual code for special control)
- Make sure "Car Templates" still has the cars assigned http://i.imgur.com/FdgmpeG.png (only Vehicle Obj needs to be assigned, others are autofilled at runtime)
- Set the "Distance Between Vehicles" to a stopping distance between vehicles (default 11)
- Set the "Road Layer" to the layer being used for roads, this is used for ground raycasting so make sure this is set correctly to a layer only used for roads
- Set the "Player Layer" to the layers being used for all colliders relating to the player, including car/bike body AND any ragdolls with colliders
- Set the "AI Layer" to the layers being used for all colliders relating to the AI vehicles
- Set the "Hit Particle" as the prefab directly from HitParticle/HitParticle.prefab (no need to drag this into the scene)
- Set the "Hit Source" as the "Hit Source" prefab you dragged into the scene (make sure you set it as the object in the scene, not the project folder!)
- Set the "Hit Sound" to Sounds/HardCrash.mp3 from the project folder
- Set the "Small Hit Sound" to Sounds/LowCrash.wav from the project folder

Creating a new traffic layout from scratch:
- Add the TrafficLaneManager.cs script to a new GameObject
- Create a child GameObject for your start position and attach LaneBezierHandler.cs
- Create a child of that GameObject for your end position and attach LaneBezierHandler.cs again
- Select the start position object and you'll have 2 selection handles to move the start and end point along with a line and 3 balls from green to red showing the direction (starts at green, ends at red) these balls can also be clicked to select the lane easier
- Tick "Is this an intersection?" if the road is an intersection (point in the road where cars can turn into new lanes or cross over other lanes) once this is ticked you'll get 2 more options: "Wait for intersection to clear?" which will make vehicles wait for the other roads on this intersection to be empty before joining the lane and "Don't wait for this lane" which will mark this lane as ignored by Wait for intersection to clear aka roads with Don't wait can have traffic but cars can enter the wait for intersection lanes as long as all lanes at this intersection NOT marked as don't wait for this lane are empty
- Once you've positioned the start and end point press "Snap all points to road surface"
- Now press "Create new lane using lane end as start" and continue creating your road lanes
- Once you've placed all your lanes make sure they're all children of your object with TrafficLaneManager.cs and right click Traffic Lane Manager in the inspector and select "Auto Group Lanes into Roads" this will automatically categorize intersections and roads and unique roads and intersections will be put into GameObjects to keep it tidy
- Next right click Traffic Lane Manager in the inspector again and select "Auto Find Lanes" this will populate the road data so it's usable by the AI script

Script notes:
- Call TrafficLaneManager.Instance.SetActiveMap(MapID) to set which map the traffic should be using (Relative to the order the TrafficData is stored in)
- Call TrafficLaneManager.Instance.SetActiveVehicle(VehicleTransform) to set the transform of the current active vehicle
- Call TrafficLaneManager.Instance.DespawnAllTraffic() to destroy and cleanup traffic (you'll need to do this before switching maps etc)
- Call TrafficLaneManager.Instance.SpawnRandomTrafficVehicle() per vehicle you want to spawn, this can fail if the attempted spawn position is blocked
- Here's a suggestion for spawning multiple vehicles when entering the level:
	for (int i = 0; TrafficLaneManager.Instance.TotalAIVehicles < 300; i++)
		TrafficLaneManager.Instance.SpawnRandomTrafficVehicle ();

Having issues or confused by my instructions? Send me an email at sean@i6.com or just ask us directly via chat for help


Change Log:
Version 1:
- Initial version

Version 2:
- Packaged all misc scripts and sounds together into the pack
- Added support for crashing into vehicles and knocking them off the road with physics
- Fixed issues related to vehicles jittering
- Timestep now affects performance lot more because it's running withing a FixedUpdate instead of an Update
- Heavily optimized car despawning/spawning by teleporting cars underground instead of using SetActive(..)
- Now caching a lot more things improving performance further
- Stripped out external script references and replaced them with set calls instead e.g SetActiveVehicle(..)
- Improved documentation and comments
- Stripped out unused code to improve readability
- Added car sparks when colliding with vehicles