from calibration_choreography.screen_marker_plugin import ScreenMarkerChoreographyPlugin
from calibration_choreography.base_plugin import ChoreographyMode


class NinePointScreenMarkerChoreography(ScreenMarkerChoreographyPlugin):
    label = "Nine-point Calibration"

    @classmethod
    def selection_label(cls) -> str:
        return "Nine-point Screen Marker"

    @staticmethod
    def get_list_of_markers_to_show(mode: ChoreographyMode) -> list:
        if ChoreographyMode.CALIBRATION == mode:
            return [
                (0.5, 0.5),
                (0.0, 1.0),
                (0.5, 1.0),
                (1.0, 1.0),
                (1.0, 0.5),
                (1.0, 0.0),
                (0.5, 0.0),
                (0.0, 0.0),
                (0.0, 0.5),
            ]
        if ChoreographyMode.VALIDATION == mode:
            return [
                (0.25, 0.5),
                (0.5, 0.75),
                (0.75, 0.5),
                (0.5, 0.25),
                (0.25, 0.0),
                (0.0, 0.25),
                (0.0, 0.75),
                (0.25, 1.0),
                (0.75, 1.0),
                (1.0, 0.75),
                (1.0, 0.25),
                (0.75, 0.0),
            ]
        raise ValueError(f"Unknown mode {mode}")
