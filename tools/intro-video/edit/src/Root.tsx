import React from 'react';
import { Composition } from 'remotion';
import { Cover, CoverFun169, CoverFunEn916, CoverFun34, CoverFun43, CoverHero34, CoverHero43, CoverWide } from './Cover';
import { Intro } from './Intro';
import { DURATION, FPS } from './timeline';

export const Root: React.FC = () => (
  <>
    <Composition id="Intro" component={Intro} durationInFrames={DURATION} fps={FPS} width={1080} height={1920} defaultProps={{ lang: 'zh' as const }} />
    <Composition id="IntroEn" component={Intro} durationInFrames={DURATION} fps={FPS} width={1080} height={1920} defaultProps={{ lang: 'en' as const }} />
    <Composition id="Cover916" component={Cover} durationInFrames={1} fps={FPS} width={1080} height={1920} defaultProps={{ tall: true }} />
    <Composition id="Cover34" component={Cover} durationInFrames={1} fps={FPS} width={1080} height={1440} defaultProps={{ tall: false }} />
    <Composition id="Cover43" component={CoverWide} durationInFrames={1} fps={FPS} width={1440} height={1080} />
    <Composition id="Hero34" component={CoverHero34} durationInFrames={1} fps={FPS} width={1080} height={1440} />
    <Composition id="Hero43" component={CoverHero43} durationInFrames={1} fps={FPS} width={1440} height={1080} />
    <Composition id="Fun34" component={CoverFun34} durationInFrames={1} fps={FPS} width={1080} height={1440} />
    <Composition id="Fun43" component={CoverFun43} durationInFrames={1} fps={FPS} width={1440} height={1080} />
    <Composition id="Fun169" component={CoverFun169} durationInFrames={1} fps={FPS} width={1920} height={1080} />
    <Composition id="FunEn916" component={CoverFunEn916} durationInFrames={1} fps={FPS} width={1080} height={1920} />
  </>
);
