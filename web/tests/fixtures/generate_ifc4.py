"""Regenerates fixtures/model-ifc4.ifc (needs native ifcopenshell). Committed output is what tests use."""
import ifcopenshell
import ifcopenshell.api.aggregate
import ifcopenshell.api.classification
import ifcopenshell.api.context
import ifcopenshell.api.material
import ifcopenshell.api.project
import ifcopenshell.api.pset
import ifcopenshell.api.root
import ifcopenshell.api.spatial
import ifcopenshell.api.unit
import ifcopenshell.guid

f = ifcopenshell.api.project.create_file(version="IFC4")
project = ifcopenshell.api.root.create_entity(f, ifc_class="IfcProject", name="Test project")
ifcopenshell.api.unit.assign_unit(f)
site = ifcopenshell.api.root.create_entity(f, ifc_class="IfcSite", name="Site")
building = ifcopenshell.api.root.create_entity(f, ifc_class="IfcBuilding", name="Building")
storey = ifcopenshell.api.root.create_entity(f, ifc_class="IfcBuildingStorey", name="Level 1")
ifcopenshell.api.aggregate.assign_object(f, products=[site], relating_object=project)
ifcopenshell.api.aggregate.assign_object(f, products=[building], relating_object=site)
ifcopenshell.api.aggregate.assign_object(f, products=[storey], relating_object=building)

w1 = ifcopenshell.api.root.create_entity(f, ifc_class="IfcWall", name="W1")
w2 = ifcopenshell.api.root.create_entity(f, ifc_class="IfcWall", name="W2")
door = ifcopenshell.api.root.create_entity(f, ifc_class="IfcDoor", name="D1")
ifcopenshell.api.spatial.assign_container(f, products=[w1, w2, door], relating_structure=storey)

pset = ifcopenshell.api.pset.add_pset(f, product=w1, name="Pset_WallCommon")
ifcopenshell.api.pset.edit_pset(f, pset=pset, properties={"FireRating": "EI60", "IsExternal": True})

concrete = ifcopenshell.api.material.add_material(f, name="Concrete", category="concrete")
ifcopenshell.api.material.assign_material(f, products=[w1], material=concrete)

uniclass = ifcopenshell.api.classification.add_classification(f, classification="Uniclass")
ifcopenshell.api.classification.add_reference(f, products=[w1], classification=uniclass, identification="Ss_25_10", name="Walls")

f.write("model-ifc4.ifc")
