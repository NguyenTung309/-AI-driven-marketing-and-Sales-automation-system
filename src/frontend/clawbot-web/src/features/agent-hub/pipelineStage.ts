export interface PipelineInfo {
  readonly stage: string;
  readonly stageLabel: string;
  readonly stageColor: string;
  readonly suggestedAction: string;
}

export function getPipelineInfo(leadScore: number, stage: string): PipelineInfo {
  if (stage === "customer" || stage === "chot") {
    return {
      stage: "chot",
      stageLabel: "Da chot",
      stageColor: "bg-success-container text-success",
      suggestedAction: "Gui feedback survey, referral program",
    };
  }
  if (leadScore >= 70 || stage === "hot") {
    return {
      stage: "sap-chot",
      stageLabel: "Sap chot",
      stageColor: "bg-warning-container text-warning",
      suggestedAction: "Goi y upsell, nhan manh uu dai",
    };
  }
  if (leadScore >= 30 || stage === "warm") {
    return {
      stage: "dang-tu-van",
      stageLabel: "Dang tu van",
      stageColor: "bg-primary/10 text-primary",
      suggestedAction: "Gui bao gia, dat lich hoc thu",
    };
  }
  return {
    stage: "moi-tiep-can",
    stageLabel: "Moi tiep can",
    stageColor: "bg-surface-variant text-on-surface-variant",
    suggestedAction: "Tim hieu nhu cau, gui brochure",
  };
}
