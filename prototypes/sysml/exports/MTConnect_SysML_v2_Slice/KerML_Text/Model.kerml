// ThinkComposer prototype 0.1.0; see companion export report.
    package MTConnectSelected {
    // source-id: a23737c1-55fc-5fef-824c-fe30a2158697
        class Observation {
        // source-id: 42ede18e-5b0e-51c1-8699-cb97ec273386
        // original-xmi-id: _19_0_3_45f01b9_1579566531115_47734_25731
        doc /* abstract entity that provides telemetry data for a {{block(DataItem)}} at a point in time.
Description */
        }
        class Component {
        // source-id: 63edb566-f60e-5aa0-bdf6-85ce4df67e1f
        // original-xmi-id: _19_0_3_45f01b9_1581734537697_742151_1119
        doc /* {{term(engineered system)}}  part of a {{term(Device)}} composed of zero or more {{termplural(Component)}} */
        }
        class Device :> Component {
        // source-id: 3302e98c-490d-5720-8050-a4dacdf3e35d
        // original-xmi-id: _19_0_3_68e0225_1623664375171_197977_2034
        doc /* {{term(Component)}} not belonging to any {{term(Component)}} that may have {{termplural(asset)}} */
            // Relationship emitted on its source declaration; check coverage report.
        }
        class DataItem {
        // source-id: dc5e4d56-ca72-5c3e-8c43-de1dcb0838a5
        // original-xmi-id: _19_0_3_68e0225_1635251889215_363828_393
        doc /* {{term(observable)}} observed by a {{term(Component)}} that may make {{termplural(Observation)}} */
        }
    }
