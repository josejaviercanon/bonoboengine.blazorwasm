export default class Stats {
    static readonly REVISION: number;
    dom: HTMLDivElement;
    domElement: HTMLDivElement;
    addPanel(panel: StatsPanel): StatsPanel;
    showPanel(id: number): void;
    begin(): void;
    end(): number;
    update(): void;
    setMode(id: number): void;
}

export interface StatsPanel {
    dom: HTMLCanvasElement;
    update(value: number, maxValue: number): void;
}
