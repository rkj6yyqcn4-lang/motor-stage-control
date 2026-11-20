clc; clear; close all;

%% =========================================================
%  HK_USB_DAQ V1.2 双轴步进位移台测试代码
%
%  控制方式：共阳极接法
%
%  X轴：
%  5VO  -> X驱动器 PUL+
%  PWM1 -> X驱动器 PUL-
%  5VO  -> X驱动器 DIR+
%  OUT3 -> X驱动器 DIR-
%
%  Y轴：
%  5VO  -> Y驱动器 PUL+
%  PWM2 -> Y驱动器 PUL-
%  5VO  -> Y驱动器 DIR+
%  OUT5 -> Y驱动器 DIR-
%
%  ENA 先不接
%
%  说明：
%  本代码不用 PWMOutSetV12，而是用 DoSetV12 手动翻转输出口。
%  这样频率低、动作慢，适合第一次调试。
%% =========================================================

%% ===== 1. DLL 路径 =====
dllPath = 'D:\驱动电机\USBDAQ_DLL_V12X64.dll';
hPath   = 'D:\驱动电机\HKDAQ_MATLAB.h';
libName = 'HKDAQ';

%% ===== 2. 端口定义 =====
% 注意 DoSetV12 的通道编号：
% PWM1 / OUT1 -> DO0
% PWM2 / OUT2 -> DO1
% OUT3        -> DO2
% OUT4        -> DO3
% OUT5        -> DO4
% OUT6        -> DO5
% OUT7        -> DO6
% OUT8        -> DO7

X.PUL_DO = 0;      % PWM1 -> X轴 PUL-
X.DIR_DO = 2;      % OUT3 -> X轴 DIR-

Y.PUL_DO = 1;      % PWM2 -> Y轴 PUL-
Y.DIR_DO = 4;      % OUT5 -> Y轴 DIR-

%% ===== 3. 测试参数 =====
pulseFreq = 500;        % 脉冲频率，第一次建议 20~100 Hz
pulseNum  = 400;       % 每次输出多少个脉冲

% 共阳极接法：
% PUL+ 接 5VO，PUL- 接采集卡输出
% 输出低电平时光耦导通，输出高电平时释放
PUL_IDLE   = 1;        % 停止状态：PUL-保持高电平
PUL_ACTIVE = 0;        % 有效脉冲：PUL-拉低

% 方向电平，0/1 都可以试
X_DIR_FORWARD = 0;
Y_DIR_FORWARD = 0;

% 测试开关
RUN_X_TEST        = true;    % 单独测试X轴
RUN_Y_TEST        = true;    % 单独测试Y轴
RUN_XY_SAME_TIME  = true;    % X/Y同时低速运动测试

%% ===== 4. 加载 DLL =====
if libisloaded(libName)
    unloadlibrary(libName);
end

loadlibrary(dllPath, hPath, 'alias', libName);
disp('DLL 加载成功');

%% ===== 5. 打开采集卡 =====
ret = calllib(libName, 'OpenUsbV12');
if ret ~= 0
    error('OpenUsbV12 打开失败，返回值 = %d', ret);
end
disp('采集卡打开成功');

cleanupObj = onCleanup(@() safeClose(libName, X, Y, PUL_IDLE));

%% ===== 6. 初始化输出状态 =====
writeDO(libName, X.PUL_DO, PUL_IDLE);
writeDO(libName, Y.PUL_DO, PUL_IDLE);

writeDO(libName, X.DIR_DO, X_DIR_FORWARD);
writeDO(libName, Y.DIR_DO, Y_DIR_FORWARD);

pause(0.2);
disp('输出口初始化完成');

%% ===== 7. 单独测试 X 轴 =====
if RUN_X_TEST
    disp('开始测试 X 轴正方向...');
    writeDO(libName, X.DIR_DO, X_DIR_FORWARD);
    pause(0.1);
    moveAxisByPulse(libName, X.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);

    pause(1);

    disp('开始测试 X 轴反方向...');
    writeDO(libName, X.DIR_DO, 1 - X_DIR_FORWARD);
    pause(0.1);
    moveAxisByPulse(libName, X.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);

    pause(1);
end

%% ===== 8. 单独测试 Y 轴 =====
if RUN_Y_TEST
    disp('开始测试 Y 轴正方向...');
    writeDO(libName, Y.DIR_DO, Y_DIR_FORWARD);
    pause(0.1);
    moveAxisByPulse(libName, Y.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);

    pause(1);

    disp('开始测试 Y 轴反方向...');
    writeDO(libName, Y.DIR_DO, 1 - Y_DIR_FORWARD);
    pause(0.1);
    moveAxisByPulse(libName, Y.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);

    pause(1);
end

%% ===== 9. X/Y 同时测试 =====
if RUN_XY_SAME_TIME
    disp('开始 X/Y 双轴同时运动测试...');

    writeDO(libName, X.DIR_DO, X_DIR_FORWARD);
    writeDO(libName, Y.DIR_DO, Y_DIR_FORWARD);
    pause(0.1);

    moveTwoAxisByPulse(libName, X.PUL_DO, Y.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);

    pause(1);

    disp('开始 X/Y 双轴同时反方向测试...');

    writeDO(libName, X.DIR_DO, 1 - X_DIR_FORWARD);
    writeDO(libName, Y.DIR_DO, 1 - Y_DIR_FORWARD);
    pause(0.1);

    moveTwoAxisByPulse(libName, X.PUL_DO, Y.PUL_DO, pulseNum, pulseFreq, PUL_ACTIVE, PUL_IDLE);
end

%% ===== 10. 关闭设备 =====
safeClose(libName, X, Y, PUL_IDLE);
disp('双轴测试结束');

%% =========================================================
%                       局部函数
%% =========================================================

function moveAxisByPulse(libName, pulDO, pulseNum, pulseFreq, activeLevel, idleLevel)
    halfPeriod = 1 / (2 * pulseFreq);

    fprintf('单轴输出：DO%d，脉冲数=%d，频率=%.1f Hz\n', ...
        pulDO, pulseNum, pulseFreq);

    for i = 1:pulseNum
        writeDO(libName, pulDO, activeLevel);
        pause(halfPeriod);

        writeDO(libName, pulDO, idleLevel);
        pause(halfPeriod);
    end

    writeDO(libName, pulDO, idleLevel);
end

function moveTwoAxisByPulse(libName, pulDO_X, pulDO_Y, pulseNum, pulseFreq, activeLevel, idleLevel)
    halfPeriod = 1 / (2 * pulseFreq);

    fprintf('双轴同时输出：X_DO%d，Y_DO%d，脉冲数=%d，频率=%.1f Hz\n', ...
        pulDO_X, pulDO_Y, pulseNum, pulseFreq);

    for i = 1:pulseNum
        writeDO(libName, pulDO_X, activeLevel);
        writeDO(libName, pulDO_Y, activeLevel);
        pause(halfPeriod);

        writeDO(libName, pulDO_X, idleLevel);
        writeDO(libName, pulDO_Y, idleLevel);
        pause(halfPeriod);
    end

    writeDO(libName, pulDO_X, idleLevel);
    writeDO(libName, pulDO_Y, idleLevel);
end

function writeDO(libName, doChan, state)
    ret = calllib(libName, 'DoSetV12', uint8(doChan), uint8(state));
    if ret ~= 0
        error('DoSetV12 输出失败：通道 DO%d，状态=%d，返回值=%d', ...
            doChan, state, ret);
    end
end

function safeClose(libName, X, Y, idleLevel)
    try
        if libisloaded(libName)
            calllib(libName, 'DoSetV12', uint8(X.PUL_DO), uint8(idleLevel));
            calllib(libName, 'DoSetV12', uint8(Y.PUL_DO), uint8(idleLevel));
            calllib(libName, 'CloseUsbV12');
            unloadlibrary(libName);
        end
    catch
    end
end