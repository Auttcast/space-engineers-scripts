# space-engineers-scripts

## Promise API

![miningShip](https://user-images.githubusercontent.com/5599653/169422155-5298b824-43ee-445b-b6a4-c5e8b311e460.png)

This is a mining ship that I built which has many moving parts.
The mining head will swing to and fro, and the pisons will extend on each iteration.
If the on-board refineries/cargo are full, then the system will wait for additional space before continuing mining.
When the main pistons have extended fully, then the secondary pisons will begin retracting in order to lower the mining head even further.
When the mining head is fully lowered, then the system will return to a "home" state.

I think that a promises api helps to make the code more readable in regards to state changes, especially when multiple chains may be evaluated at the same time. 
This is demonstrated in the MainOperation function of the miningShipSampleScript.cs
