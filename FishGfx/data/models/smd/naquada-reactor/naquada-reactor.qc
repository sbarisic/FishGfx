// Created by Crowbar 0.51.0.0

$modelname "Naquada-Reactor.mdl"

$bodygroup "studio"
{
	studio "naquada-reactor_Naduada-Reactor_Ref.smd"
}


$surfaceprop "metal"

$contents "solid"

$cdmaterials "models\"

$cbox 0 0 0 0 0 0

$bbox -5.233 -17.869 -0.22 5.233 18.01 14.279


$sequence "idle" {
	"naquada-reactor_anims\idle.smd"
	fadein 0.2
	fadeout 0.2
	fps 1
}

$sequence "open" {
	"naquada-reactor_anims\open.smd"
	fadein 0.2
	fadeout 0.2
	fps 13
}

$sequence "close" {
	"naquada-reactor_anims\close.smd"
	fadein 0.2
	fadeout 0.2
	fps 13
}

$collisionmodel "naquada-reactor_physics.smd"
{
	$mass 5.719099
	$inertia 1
	$damping 0
	$rotdamping 0
	$rootbone " "
	$concave

}

$keyvalues
{
	prop_data 
	{
		"base"		"Metal.Medium"
	}
}
